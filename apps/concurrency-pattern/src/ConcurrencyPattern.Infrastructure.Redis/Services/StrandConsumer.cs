using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

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
/// 1. Redis Set에서 활성 Strand 목록 조회
/// 2. 각 Strand에 대해 분산 Lock 획득 시도
/// 3. Lock 획득 성공 시 해당 Strand의 Queue 처리
/// 4. 처리 완료 또는 Batch Timeout 시 Lock 해제
/// 5. 다른 Consumer가 해당 Strand 인계 가능
/// </summary>
public class StrandConsumer : BackgroundService
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly IRedisDistributedLock _distributedLock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RedisSettings _settings;
    private readonly ILogger<StrandConsumer> _logger;

    private readonly string _strandRegistryKey;
    private readonly string _queuePrefix;
    private readonly string _consumerId;

    // 현재 이 Consumer가 소유한 Strand 목록
    private readonly ConcurrentDictionary<string, Task> _ownedStrands = new();

    // 설정값
    private readonly TimeSpan _strandLeaseTime = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _batchTimeout = TimeSpan.FromSeconds(5);
    private readonly int _maxBatchSize = 100;
    private readonly TimeSpan _discoveryInterval = TimeSpan.FromSeconds(2);

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

        _strandRegistryKey = $"{_settings.InstanceName}:strand-registry";
        _queuePrefix = $"{_settings.InstanceName}:queue:";
        _consumerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

        _logger.LogInformation("StrandConsumer initialized: {ConsumerId}", _consumerId);
    }

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

        // Graceful shutdown: 소유한 모든 Strand 해제
        await ReleaseAllStrandsAsync();
    }

    /// <summary>
    /// 활성 Strand 발견 및 소유권 획득 시도
    /// </summary>
    private async Task DiscoverAndClaimStrandsAsync(CancellationToken ct)
    {
        var db = _connectionManager.GetDatabase();

        // 1. 활성 Strand 목록 조회 (Queue가 존재하는 Entity들)
        var activeStrands = await DiscoverActiveStrandsAsync(db);

        foreach (var strandKey in activeStrands)
        {
            if (ct.IsCancellationRequested) break;

            // 이미 소유 중인 Strand는 스킵
            if (_ownedStrands.ContainsKey(strandKey)) continue;

            // 2. Strand Lock 획득 시도 (Non-blocking)
            var lockResource = $"strand:{strandKey}";
            if (await _distributedLock.TryAcquireAsync(lockResource, _strandLeaseTime, ct))
            {
                _logger.LogInformation("Claimed strand: {Strand} by {Consumer}", strandKey, _consumerId);

                // 3. Strand 처리 Task 시작
                var processingTask = ProcessStrandAsync(strandKey, lockResource, ct);
                _ownedStrands.TryAdd(strandKey, processingTask);
            }
        }

        // 완료된 Task 정리
        CleanupCompletedStrands();
    }

    /// <summary>
    /// Queue Key 패턴에서 활성 Strand 목록 발견
    /// </summary>
    private async Task<IEnumerable<string>> DiscoverActiveStrandsAsync(IDatabase db)
    {
        var server = _connectionManager.GetServer();
        var pattern = $"{_queuePrefix}*";

        var strands = new HashSet<string>();

        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            // queue:Account:123 → Account:123
            var strandKey = key.ToString().Replace(_queuePrefix, "");
            strands.Add(strandKey);
        }

        return strands;
    }

    /// <summary>
    /// 단일 Strand 처리 (순차 처리 보장)
    /// </summary>
    private async Task ProcessStrandAsync(string strandKey, string lockResource, CancellationToken ct)
    {
        var queueKey = $"{_queuePrefix}{strandKey}";
        var db = _connectionManager.GetDatabase();

        try
        {
            var processedCount = 0;
            var batchStart = DateTime.UtcNow;

            while (!ct.IsCancellationRequested)
            {
                // Batch Timeout 또는 Max Batch Size 도달 시 Lock 갱신 또는 해제
                if (processedCount >= _maxBatchSize ||
                    DateTime.UtcNow - batchStart > _batchTimeout)
                {
                    // Lock 갱신 (Lease 연장) 또는 다른 Consumer에게 양보
                    if (await ShouldContinueProcessingAsync(db, queueKey))
                    {
                        await RefreshLockAsync(lockResource);
                        processedCount = 0;
                        batchStart = DateTime.UtcNow;
                    }
                    else
                    {
                        break; // 큐가 비었으면 종료
                    }
                }

                // Queue에서 Command 가져오기 (Non-blocking)
                var commandData = await db.ListRightPopAsync(queueKey);

                if (commandData.IsNullOrEmpty)
                {
                    // 큐가 비었으면 잠시 대기 후 재확인
                    await Task.Delay(100, ct);

                    // 일정 시간 동안 비어있으면 Lock 해제
                    if (!await ShouldContinueProcessingAsync(db, queueKey))
                    {
                        break;
                    }
                    continue;
                }

                // Command 처리
                await ProcessCommandAsync(commandData!, strandKey, ct);
                processedCount++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing strand: {Strand}", strandKey);
        }
        finally
        {
            // Lock 해제
            await _distributedLock.ReleaseAsync(lockResource);
            _ownedStrands.TryRemove(strandKey, out _);
            _logger.LogInformation("Released strand: {Strand} by {Consumer}", strandKey, _consumerId);
        }
    }

    /// <summary>
    /// 처리 계속 여부 판단 (큐 길이 확인)
    /// </summary>
    private async Task<bool> ShouldContinueProcessingAsync(IDatabase db, string queueKey)
    {
        var length = await db.ListLengthAsync(queueKey);
        return length > 0;
    }

    /// <summary>
    /// Lock Lease 갱신
    /// </summary>
    private async Task RefreshLockAsync(string lockResource)
    {
        // Lock 해제 후 재획득 (Lease 연장)
        await _distributedLock.ReleaseAsync(lockResource);
        await _distributedLock.TryAcquireAsync(lockResource, _strandLeaseTime);
    }

    /// <summary>
    /// Command 실행
    /// </summary>
    private async Task ProcessCommandAsync(RedisValue commandData, string strandKey, CancellationToken ct)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<RedisCommandEnvelope>(commandData!);
            if (envelope == null)
            {
                _logger.LogWarning("Failed to deserialize command for strand: {Strand}", strandKey);
                return;
            }

            _logger.LogDebug("Processing command {CommandId} for strand {Strand}",
                envelope.CommandId, strandKey);

            // Handler 실행 (DI Container 사용)
            using var scope = _scopeFactory.CreateScope();
            var result = await ExecuteCommandHandlerAsync(scope.ServiceProvider, envelope, ct);

            // 결과 발행 (Response Channel)
            if (!string.IsNullOrEmpty(envelope.ResponseChannel))
            {
                var db = _connectionManager.GetDatabase();
                var resultJson = JsonSerializer.Serialize(result);
                await db.PublishAsync(RedisChannel.Literal(envelope.ResponseChannel), resultJson);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing command for strand: {Strand}", strandKey);
            // TODO: Dead Letter Queue로 이동
        }
    }

    /// <summary>
    /// Command Handler 실행 (Reflection 기반)
    /// </summary>
    private async Task<RedisCommandResult> ExecuteCommandHandlerAsync(
        IServiceProvider serviceProvider,
        RedisCommandEnvelope envelope,
        CancellationToken ct)
    {
        try
        {
            var commandType = Type.GetType(envelope.CommandType!);
            if (commandType == null)
            {
                return new RedisCommandResult
                {
                    CommandId = envelope.CommandId,
                    Success = false,
                    ErrorMessage = $"Command type not found: {envelope.CommandType}"
                };
            }

            var command = JsonSerializer.Deserialize(envelope.CommandData!, commandType);
            if (command == null)
            {
                return new RedisCommandResult
                {
                    CommandId = envelope.CommandId,
                    Success = false,
                    ErrorMessage = "Failed to deserialize command"
                };
            }

            // ICommandHandler<TCommand, TResult> 찾기
            var resultType = Type.GetType(envelope.ResultType!);
            var handlerType = typeof(Core.Interfaces.ICommandHandler<,>)
                .MakeGenericType(commandType, resultType!);

            var handler = serviceProvider.GetService(handlerType);
            if (handler == null)
            {
                return new RedisCommandResult
                {
                    CommandId = envelope.CommandId,
                    Success = false,
                    ErrorMessage = $"Handler not found for: {commandType.Name}"
                };
            }

            // HandleAsync 호출
            var handleMethod = handlerType.GetMethod("HandleAsync");
            var resultTask = (Task)handleMethod!.Invoke(handler, new[] { command, ct })!;
            await resultTask;

            var resultProperty = resultTask.GetType().GetProperty("Result");
            var resultValue = resultProperty!.GetValue(resultTask);

            return new RedisCommandResult
            {
                CommandId = envelope.CommandId,
                Success = true,
                ResultData = JsonSerializer.Serialize(resultValue),
                ResultType = envelope.ResultType
            };
        }
        catch (Exception ex)
        {
            return new RedisCommandResult
            {
                CommandId = envelope.CommandId,
                Success = false,
                ErrorMessage = ex.Message
            };
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
            _ownedStrands.TryRemove(strand, out _);
        }
    }

    /// <summary>
    /// 모든 소유 Strand 해제 (Graceful Shutdown)
    /// </summary>
    private async Task ReleaseAllStrandsAsync()
    {
        _logger.LogInformation("Releasing all strands for consumer: {ConsumerId}", _consumerId);

        foreach (var strand in _ownedStrands.Keys)
        {
            try
            {
                await _distributedLock.ReleaseAsync($"strand:{strand}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing strand: {Strand}", strand);
            }
        }

        _ownedStrands.Clear();
    }
}
