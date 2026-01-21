using System.Text.Json;
using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// 컨텍스트(EntityType)별 독립 Consumer
///
/// 핵심 원리:
/// - 각 컨텍스트(Account, Inventory 등)별로 독립된 Consumer 인스턴스
/// - 컨텍스트 내 엔티티별로 별도 Task 실행 → 엔티티 간 병렬, 엔티티 내 순차
/// - 컨텍스트 간 완전 독립 → 컨텍스트 간 완전 병렬
/// </summary>
public class RedisContextConsumer : IDisposable
{
    private readonly string _contextName;  // "Account", "Inventory" 등
    private readonly IRedisConnectionManager _connectionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly RedisSettings _settings;
    private readonly ILogger _logger;

    private readonly string _queuePrefix;
    private readonly Dictionary<string, Task> _entityTasks = new();
    private readonly SemaphoreSlim _taskLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    public string ContextName => _contextName;
    public bool IsRunning { get; private set; }

    public RedisContextConsumer(
        string contextName,
        IRedisConnectionManager connectionManager,
        IServiceProvider serviceProvider,
        IOptions<RedisSettings> settings,
        ILogger logger)
    {
        _contextName = contextName;
        _connectionManager = connectionManager;
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
        _queuePrefix = $"{_settings.InstanceName}:queue:{contextName}:";
    }

    /// <summary>
    /// 컨텍스트 Consumer 시작
    /// </summary>
    public async Task StartAsync(CancellationToken externalToken)
    {
        IsRunning = true;
        _logger.LogInformation("Context consumer started: {Context}", _contextName);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _cts.Token);
        var ct = linkedCts.Token;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DiscoverAndProcessEntitiesAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in context consumer: {Context}", _contextName);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }

        IsRunning = false;
        _logger.LogInformation("Context consumer stopped: {Context}", _contextName);
    }

    /// <summary>
    /// 해당 컨텍스트의 엔티티별 큐 검색 및 처리 Task 시작
    /// </summary>
    private async Task DiscoverAndProcessEntitiesAsync(CancellationToken ct)
    {
        var server = _connectionManager.GetServer();
        var pattern = $"{_queuePrefix}*";

        await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(ct))
        {
            var queueKey = key.ToString();

            await _taskLock.WaitAsync(ct);
            try
            {
                // 이미 처리 중인 큐는 스킵
                if (_entityTasks.TryGetValue(queueKey, out var existingTask))
                {
                    if (!existingTask.IsCompleted) continue;
                    _entityTasks.Remove(queueKey);
                }

                // 새 엔티티 큐에 대한 처리 Task 시작
                var task = ProcessEntityQueueAsync(queueKey, ct);
                _entityTasks[queueKey] = task;

                _logger.LogDebug("Started entity consumer: {Queue}", queueKey);
            }
            finally
            {
                _taskLock.Release();
            }
        }
    }

    /// <summary>
    /// 단일 엔티티 큐 처리 (순차적)
    /// </summary>
    private async Task ProcessEntityQueueAsync(string queueKey, CancellationToken ct)
    {
        var db = _connectionManager.GetDatabase();
        var idleCount = 0;
        const int maxIdleCount = 100;  // 10초간 비어있으면 Task 종료

        while (!ct.IsCancellationRequested && idleCount < maxIdleCount)
        {
            try
            {
                var result = await db.ListRightPopAsync(queueKey);

                if (result.IsNullOrEmpty)
                {
                    idleCount++;
                    await Task.Delay(100, ct);
                    continue;
                }

                idleCount = 0;  // 리셋
                await ProcessCommandAsync(result!, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing entity queue: {Queue}", queueKey);
                await Task.Delay(100, ct);
            }
        }

        _logger.LogDebug("Entity consumer ended (idle): {Queue}", queueKey);
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

            _logger.LogDebug("Processing command: {CommandId} in context {Context}",
                envelope.CommandId, _contextName);

            var result = await ExecuteCommandAsync(envelope, ct);
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

    private async Task PublishResultAsync(string channel, RedisCommandResult result)
    {
        try
        {
            var subscriber = _connectionManager.GetSubscriber();
            var resultJson = JsonSerializer.Serialize(result);

            await subscriber.PublishAsync(
                RedisChannel.Literal(channel),
                resultJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing result to channel: {Channel}", channel);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _taskLock.Dispose();
    }
}
