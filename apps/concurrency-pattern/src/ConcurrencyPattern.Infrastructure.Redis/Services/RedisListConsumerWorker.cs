using System.Text.Json;
using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// Redis List Consumer Worker (간소화 버전)
///
/// 동작 원리:
/// - BRPOP으로 큐에서 커맨드를 하나씩 꺼냄 (블로킹, 원자적)
/// - 커맨드 실행 후 결과를 Pub/Sub으로 발행
/// - 같은 큐에서 BRPOP하는 Consumer가 하나이므로 순차 처리 보장
/// - 다른 엔티티 큐는 별도 Task에서 병렬 처리
///
/// 분산 환경:
/// - 같은 엔티티 큐를 여러 Consumer가 BRPOP하면 요청이 분산됨
/// - 순차 처리가 필요하면 엔티티당 Consumer 하나만 유지
/// </summary>
public class RedisListConsumerWorker : BackgroundService
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisListConsumerWorker> _logger;

    private readonly string _queuePrefix;
    private readonly HashSet<string> _activeQueues = new();
    private readonly Dictionary<string, Task> _queueTasks = new();
    private readonly SemaphoreSlim _queueLock = new(1, 1);

    public RedisListConsumerWorker(
        IRedisConnectionManager connectionManager,
        IServiceProvider serviceProvider,
        IOptions<RedisSettings> settings,
        ILogger<RedisListConsumerWorker> logger)
    {
        _connectionManager = connectionManager;
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
        _queuePrefix = $"{_settings.InstanceName}:queue:";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Redis List Consumer Worker starting...");

        // 주기적으로 새 큐 검색 및 Consumer 시작
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DiscoverAndProcessQueuesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in queue discovery loop");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        _logger.LogInformation("Redis List Consumer Worker stopping...");
    }

    /// <summary>
    /// 큐 검색 및 Consumer Task 시작
    /// </summary>
    private async Task DiscoverAndProcessQueuesAsync(CancellationToken ct)
    {
        var server = _connectionManager.GetServer();
        var pattern = $"{_queuePrefix}*";

        await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(ct))
        {
            var queueKey = key.ToString();

            await _queueLock.WaitAsync(ct);
            try
            {
                if (!_activeQueues.Contains(queueKey))
                {
                    _activeQueues.Add(queueKey);
                    var task = ProcessQueueAsync(queueKey, ct);
                    _queueTasks[queueKey] = task;
                    _logger.LogInformation("Started consumer for queue: {Queue}", queueKey);
                }
            }
            finally
            {
                _queueLock.Release();
            }
        }
    }

    /// <summary>
    /// 단일 큐 처리 (BRPOP 루프)
    /// 이 메서드가 해당 큐의 유일한 Consumer이므로 순차 처리 보장
    /// </summary>
    private async Task ProcessQueueAsync(string queueKey, CancellationToken ct)
    {
        var db = _connectionManager.GetDatabase();

        _logger.LogDebug("Consumer started for queue: {Queue}", queueKey);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // BRPOP: 블로킹으로 오른쪽에서 하나씩 꺼냄 (FIFO)
                // 타임아웃 5초 - 타임아웃 후 다시 시도
                var result = await db.ListRightPopAsync(queueKey);

                if (result.IsNullOrEmpty)
                {
                    // 큐가 비어있으면 잠시 대기 후 재시도
                    await Task.Delay(100, ct);
                    continue;
                }

                await ProcessCommandAsync(result!, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue: {Queue}", queueKey);
                await Task.Delay(100, ct);
            }
        }

        _logger.LogDebug("Consumer stopped for queue: {Queue}", queueKey);
    }

    /// <summary>
    /// 커맨드 처리 및 결과 발행
    /// </summary>
    private async Task ProcessCommandAsync(string envelopeJson, CancellationToken ct)
    {
        RedisCommandEnvelope? envelope = null;

        try
        {
            envelope = JsonSerializer.Deserialize<RedisCommandEnvelope>(envelopeJson);
            if (envelope == null)
            {
                _logger.LogWarning("Failed to deserialize command envelope");
                return;
            }

            _logger.LogDebug("Processing command: {CommandId}", envelope.CommandId);

            // 커맨드 실행
            var result = await ExecuteCommandAsync(envelope, ct);

            // 결과 발행 (Pub/Sub)
            await PublishResultAsync(envelope.ResponseChannel, result);

            _logger.LogDebug("Command completed: {CommandId}", envelope.CommandId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing command: {CommandId}", envelope?.CommandId);

            if (envelope != null)
            {
                var errorResult = RedisCommandResult.FromError(envelope.CommandId, ex);
                await PublishResultAsync(envelope.ResponseChannel, errorResult);
            }
        }
    }

    /// <summary>
    /// 커맨드 실행
    /// </summary>
    private async Task<RedisCommandResult> ExecuteCommandAsync(
        RedisCommandEnvelope envelope,
        CancellationToken ct)
    {
        try
        {
            var command = envelope.DeserializeCommand();

            using var scope = _serviceProvider.CreateScope();

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
            _logger.LogError(ex, "Error executing command: {CommandId}", envelope.CommandId);
            return RedisCommandResult.FromError(envelope.CommandId, ex);
        }
    }

    /// <summary>
    /// 결과를 Pub/Sub으로 발행
    /// </summary>
    private async Task PublishResultAsync(string channel, RedisCommandResult result)
    {
        try
        {
            var subscriber = _connectionManager.GetSubscriber();
            var resultJson = JsonSerializer.Serialize(result);

            await subscriber.PublishAsync(
                RedisChannel.Literal(channel),
                resultJson);

            _logger.LogDebug("Result published to channel: {Channel}", channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing result to channel: {Channel}", channel);
        }
    }
}
