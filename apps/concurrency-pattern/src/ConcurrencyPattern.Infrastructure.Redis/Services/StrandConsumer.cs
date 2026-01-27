using System.Collections.Concurrent;
using System.Text.Json;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// Strand 기반 분산 Consumer
///
/// Boost.Asio Strand 패턴 적용:
/// - 동일 Entity(Strand) 내 순차 처리 보장
/// - 다른 Entity(Strand) 간 병렬 처리
/// - N개 Consumer 프로세스 지원 (SPOF 제거)
///
/// 동작 원리:
/// 1. KEYS 스캔으로 활성 Strand(Entity Queue) 발견
/// 2. 각 Strand에 대해 분산 Lock 획득 시도 (Non-blocking)
/// 3. Lock 획득 성공 시 해당 Strand의 Queue를 순차 처리
/// 4. Batch 완료 또는 큐가 비면 Lock 해제 → 다른 Consumer가 인계 가능
/// 5. Consumer 장애 시 Lock TTL 만료 후 자동 인계
///
///   Consumer 1          Consumer 2          Consumer 3
///   ┌─────────┐        ┌─────────┐        ┌─────────┐
///   │Lock:A:1 │        │Lock:A:2 │        │Lock:B:1 │
///   └────┬────┘        └────┬────┘        └────┬────┘
///        ↓                  ↓                  ↓
///   Queue:A:1          Queue:A:2          Queue:B:1
///   (순차처리)          (순차처리)          (순차처리)
/// </summary>
public class StrandConsumer : BackgroundService
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly IRedisDistributedLock _distributedLock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RedisSettings _settings;
    private readonly ILogger _logger;

    private readonly string _queuePrefix;
    private readonly string _consumerId;

    /// <summary>
    /// 현재 이 Consumer가 소유한 Strand 목록
    /// Key: strandKey (예: "Account:123"), Value: 처리 Task
    /// </summary>
    private readonly ConcurrentDictionary<string, Task> _ownedStrands = new();

    // Strand 설정
    private readonly TimeSpan _strandLeaseTime;
    private readonly TimeSpan _batchTimeout;
    private readonly int _maxBatchSize;
    private readonly TimeSpan _discoveryInterval;
    private readonly int _maxIdleIterations;

    public StrandConsumer(
        IRedisConnectionManager connectionManager,
        IRedisDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        IOptions<RedisSettings> settings,
        ILogger<StrandConsumer> logger)
    {
        _connectionManager = connectionManager;
        _distributedLock = distributedLock;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;

        _queuePrefix = $"{_settings.InstanceName}:queue:";
        _consumerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

        // Strand 설정 (RedisSettings에서 로드)
        _strandLeaseTime = TimeSpan.FromSeconds(_settings.StrandLeaseSeconds);
        _batchTimeout = TimeSpan.FromSeconds(_settings.StrandBatchTimeoutSeconds);
        _maxBatchSize = _settings.StrandMaxBatchSize;
        _discoveryInterval = TimeSpan.FromSeconds(_settings.StrandDiscoveryIntervalSeconds);
        _maxIdleIterations = _settings.StrandMaxIdleIterations;

        _logger.LogInformation(
            "StrandConsumer initialized: {ConsumerId}, Lease={Lease}s, Batch={Batch}s/{MaxBatch}, Discovery={Discovery}s",
            _consumerId, _strandLeaseTime.TotalSeconds, _batchTimeout.TotalSeconds,
            _maxBatchSize, _discoveryInterval.TotalSeconds);
    }

    /// <summary>
    /// 내부 생성자 (EventDrivenStrandConsumer용)
    /// </summary>
    internal StrandConsumer(
        IRedisConnectionManager connectionManager,
        IRedisDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        IOptions<RedisSettings> settings,
        ILogger logger,
        string? overrideConsumerId)
    {
        _connectionManager = connectionManager;
        _distributedLock = distributedLock;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;

        _queuePrefix = $"{_settings.InstanceName}:queue:";
        _consumerId = overrideConsumerId ?? $"{Environment.MachineName}:{Guid.NewGuid():N}";

        _strandLeaseTime = TimeSpan.FromSeconds(_settings.StrandLeaseSeconds);
        _batchTimeout = TimeSpan.FromSeconds(_settings.StrandBatchTimeoutSeconds);
        _maxBatchSize = _settings.StrandMaxBatchSize;
        _discoveryInterval = TimeSpan.FromSeconds(_settings.StrandDiscoveryIntervalSeconds);
        _maxIdleIterations = _settings.StrandMaxIdleIterations;
    }

    // 서브클래스에서 접근 가능하도록 protected
    protected string ConsumerId => _consumerId;
    protected IRedisConnectionManager ConnectionManager => _connectionManager;
    protected IRedisDistributedLock DistributedLock => _distributedLock;
    protected RedisSettings Settings => _settings;
    protected ConcurrentDictionary<string, Task> OwnedStrands => _ownedStrands;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StrandConsumer starting: {ConsumerId}", _consumerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DiscoverAndClaimStrandsAsync(stoppingToken);
                await Task.Delay(_discoveryInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in strand discovery loop");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        await ReleaseAllStrandsAsync();
        _logger.LogInformation("StrandConsumer stopped: {ConsumerId}", _consumerId);
    }

    /// <summary>
    /// 활성 Strand 발견 및 소유권 획득 시도
    /// </summary>
    private async Task DiscoverAndClaimStrandsAsync(CancellationToken ct)
    {
        var activeStrands = await DiscoverActiveStrandsAsync();

        foreach (var strandKey in activeStrands)
        {
            if (ct.IsCancellationRequested) break;

            // 이미 소유 중인 Strand는 스킵
            if (_ownedStrands.ContainsKey(strandKey)) continue;

            // Strand Lock 획득 시도 (Non-blocking)
            var lockResource = $"strand:{strandKey}";
            if (await _distributedLock.TryAcquireAsync(lockResource, _strandLeaseTime, ct))
            {
                _logger.LogInformation("Claimed strand: {Strand} by {Consumer}", strandKey, _consumerId);

                var processingTask = ProcessStrandAsync(strandKey, lockResource, ct);
                _ownedStrands.TryAdd(strandKey, processingTask);
            }
        }

        // 완료된 Task 정리
        CleanupCompletedStrands();
    }

    /// <summary>
    /// 추가 Strand를 즉시 claim (EventDrivenStrandConsumer에서 호출)
    /// </summary>
    internal async Task TryClaimStrandAsync(string strandKey, CancellationToken ct)
    {
        if (_ownedStrands.ContainsKey(strandKey)) return;

        var lockResource = $"strand:{strandKey}";
        if (await _distributedLock.TryAcquireAsync(lockResource, _strandLeaseTime, ct))
        {
            _logger.LogInformation("Claimed strand (event-driven): {Strand} by {Consumer}", strandKey, _consumerId);

            var processingTask = ProcessStrandAsync(strandKey, lockResource, ct);
            _ownedStrands.TryAdd(strandKey, processingTask);
        }
    }

    /// <summary>
    /// Queue Key 패턴에서 활성 Strand 목록 발견
    /// </summary>
    private async Task<IReadOnlyList<string>> DiscoverActiveStrandsAsync()
    {
        var server = _connectionManager.GetServer();
        var pattern = $"{_queuePrefix}*";
        var strands = new List<string>();

        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            // "ConcurrencyPattern:queue:Account:123" → "Account:123"
            var strandKey = key.ToString().Replace(_queuePrefix, "");
            strands.Add(strandKey);
        }

        return strands;
    }

    /// <summary>
    /// 단일 Strand의 Queue를 순차 처리
    ///
    /// 처리 흐름:
    /// 1. Queue에서 RPOP으로 커맨드 꺼내기
    /// 2. 커맨드 핸들러 실행 (DI Scope)
    /// 3. 결과를 Pub/Sub으로 응답
    /// 4. Batch Timeout 또는 MaxBatchSize 도달 시 Lock TTL 갱신
    /// 5. 큐가 비면 Idle 대기 후 Lock 해제
    /// </summary>
    private async Task ProcessStrandAsync(string strandKey, string lockResource, CancellationToken ct)
    {
        var queueKey = $"{_queuePrefix}{strandKey}";
        var db = _connectionManager.GetDatabase();

        try
        {
            var processedCount = 0;
            var batchStart = DateTime.UtcNow;
            var idleCount = 0;

            while (!ct.IsCancellationRequested)
            {
                // Batch 경계: Lock TTL 갱신
                if (processedCount >= _maxBatchSize ||
                    DateTime.UtcNow - batchStart > _batchTimeout)
                {
                    var queueLength = await db.ListLengthAsync(queueKey);
                    if (queueLength > 0)
                    {
                        // 원자적 Lock TTL 갱신 (Race Condition 없음)
                        if (!await _distributedLock.RefreshAsync(lockResource, _strandLeaseTime))
                        {
                            _logger.LogWarning("Lost strand ownership: {Strand}", strandKey);
                            break;
                        }
                        processedCount = 0;
                        batchStart = DateTime.UtcNow;
                    }
                    else
                    {
                        break; // 큐가 비었으면 종료
                    }
                }

                // Queue에서 Command 가져오기
                var commandData = await db.ListRightPopAsync(queueKey);

                if (commandData.IsNullOrEmpty)
                {
                    idleCount++;
                    if (idleCount >= _maxIdleIterations)
                    {
                        break; // Idle Timeout → Lock 해제
                    }
                    await Task.Delay(100, ct);
                    continue;
                }

                idleCount = 0;
                await ExecuteEnvelopeAsync(commandData!, strandKey, ct);
                processedCount++;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing strand: {Strand}", strandKey);
        }
        finally
        {
            await _distributedLock.ReleaseAsync(lockResource);
            _ownedStrands.TryRemove(strandKey, out _);
            _logger.LogInformation("Released strand: {Strand} by {Consumer}", strandKey, _consumerId);
        }
    }

    /// <summary>
    /// Envelope 역직렬화 → Handler 실행 → 결과 Pub/Sub 발행
    /// </summary>
    private async Task ExecuteEnvelopeAsync(RedisValue commandData, string strandKey, CancellationToken ct)
    {
        RedisCommandEnvelope? envelope = null;

        try
        {
            envelope = JsonSerializer.Deserialize<RedisCommandEnvelope>(commandData!);
            if (envelope == null)
            {
                _logger.LogWarning("Failed to deserialize command for strand: {Strand}", strandKey);
                return;
            }

            _logger.LogDebug("Processing command {CommandId} on strand {Strand} by {Consumer}",
                envelope.CommandId, strandKey, _consumerId);

            var result = await ExecuteCommandAsync(envelope, ct);
            await PublishResultAsync(envelope.ResponseChannel, result);

            _logger.LogDebug("Command completed: {CommandId} on strand {Strand}",
                envelope.CommandId, strandKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {CommandId} on strand {Strand}",
                envelope?.CommandId, strandKey);

            if (envelope != null)
            {
                var errorResult = RedisCommandResult.FromError(envelope.CommandId, ex);
                await PublishResultAsync(envelope.ResponseChannel, errorResult);
            }
        }
    }

    /// <summary>
    /// 커맨드 핸들러 실행 (DI Scope + Reflection)
    /// RedisContextConsumer.ExecuteCommandAsync 패턴과 동일
    /// </summary>
    private async Task<RedisCommandResult> ExecuteCommandAsync(
        RedisCommandEnvelope envelope,
        CancellationToken ct)
    {
        try
        {
            var command = envelope.DeserializeCommand();

            using var scope = _scopeFactory.CreateScope();

            var commandType = Type.GetType(envelope.CommandType)
                ?? throw new InvalidOperationException($"Cannot resolve command type: {envelope.CommandType}");
            var resultType = Type.GetType(envelope.ResultType)
                ?? throw new InvalidOperationException($"Cannot resolve result type: {envelope.ResultType}");

            var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, resultType);
            var handler = scope.ServiceProvider.GetRequiredService(handlerType);

            var handleMethod = handlerType.GetMethod("HandleAsync")
                ?? throw new InvalidOperationException("HandleAsync method not found");

            var resultTask = (Task)handleMethod.Invoke(handler, new object[] { command, ct })!;
            await resultTask;

            var resultProperty = resultTask.GetType().GetProperty("Result");
            var result = resultProperty?.GetValue(resultTask);

            return new RedisCommandResult
            {
                CommandId = envelope.CommandId,
                Success = true,
                ResultType = envelope.ResultType,
                ResultData = JsonSerializer.Serialize(result)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command handler: {CommandId}", envelope.CommandId);
            return RedisCommandResult.FromError(envelope.CommandId, ex);
        }
    }

    /// <summary>
    /// 결과를 Pub/Sub 채널로 발행
    /// </summary>
    private async Task PublishResultAsync(string channel, RedisCommandResult result)
    {
        if (string.IsNullOrEmpty(channel)) return;

        try
        {
            var subscriber = _connectionManager.GetSubscriber();
            var resultJson = JsonSerializer.Serialize(result);
            await subscriber.PublishAsync(RedisChannel.Literal(channel), resultJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing result to channel: {Channel}", channel);
        }
    }

    /// <summary>
    /// 완료된 Strand Task 정리
    /// </summary>
    private void CleanupCompletedStrands()
    {
        var completedStrands = _ownedStrands
            .Where(kvp => kvp.Value.IsCompleted)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var strand in completedStrands)
        {
            if (_ownedStrands.TryRemove(strand, out var task) && task.IsFaulted)
            {
                _logger.LogWarning(task.Exception, "Strand task faulted: {Strand}", strand);
            }
        }
    }

    /// <summary>
    /// 모든 소유 Strand Lock 해제 (Graceful Shutdown)
    /// </summary>
    private async Task ReleaseAllStrandsAsync()
    {
        _logger.LogInformation("Releasing all strands for consumer: {ConsumerId}", _consumerId);

        var tasks = _ownedStrands.Keys.Select(async strand =>
        {
            try
            {
                await _distributedLock.ReleaseAsync($"strand:{strand}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing strand: {Strand}", strand);
            }
        });

        await Task.WhenAll(tasks);
        _ownedStrands.Clear();
    }
}
