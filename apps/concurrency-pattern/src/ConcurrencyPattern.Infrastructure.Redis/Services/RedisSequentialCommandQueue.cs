using System.Collections.Concurrent;
using System.Text.Json;
using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using ConcurrencyPattern.SequentialProcessor.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// Redis Streams + Pub/Sub 기반 순차 처리 큐
///
/// 흐름:
/// 1. Producer: 커맨드를 Redis Stream에 추가 (XADD)
/// 2. Producer: Pub/Sub 채널 구독하고 결과 대기
/// 3. Consumer Worker: Stream에서 커맨드 읽기 (XREADGROUP)
/// 4. Consumer Worker: 커맨드 실행 후 결과를 Pub/Sub으로 발행
/// 5. Producer: 결과 수신 후 반환
/// </summary>
public class RedisSequentialCommandQueue : ISequentialCommandQueue, IDisposable
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly IRedisDistributedLock _distributedLock;
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisSequentialCommandQueue> _logger;

    private readonly string _streamPrefix;
    private readonly string _responseChannelPrefix;

    // 응답 대기 중인 커맨드들
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RedisCommandResult>> _pendingCommands = new();

    private ISubscriber? _subscriber;
    private bool _isSubscribed;

    public RedisSequentialCommandQueue(
        IRedisConnectionManager connectionManager,
        IRedisDistributedLock distributedLock,
        IOptions<RedisSettings> settings,
        ILogger<RedisSequentialCommandQueue> logger)
    {
        _connectionManager = connectionManager;
        _distributedLock = distributedLock;
        _settings = settings.Value;
        _logger = logger;

        _streamPrefix = $"{_settings.InstanceName}:stream:";
        _responseChannelPrefix = $"{_settings.InstanceName}:response:";
    }

    /// <summary>
    /// 응답 채널 구독 시작
    /// </summary>
    private async Task EnsureSubscribedAsync()
    {
        if (_isSubscribed) return;

        _subscriber = _connectionManager.GetSubscriber();

        // 와일드카드 패턴으로 모든 응답 채널 구독
        var pattern = new RedisChannel($"{_responseChannelPrefix}*", RedisChannel.PatternMode.Pattern);

        await _subscriber.SubscribeAsync(pattern, (channel, message) =>
        {
            try
            {
                var result = JsonSerializer.Deserialize<RedisCommandResult>(message!);
                if (result != null && _pendingCommands.TryRemove(result.CommandId, out var tcs))
                {
                    tcs.TrySetResult(result);
                    _logger.LogDebug("Response received for command {CommandId}", result.CommandId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing response from channel {Channel}", channel);
            }
        });

        _isSubscribed = true;
        _logger.LogInformation("Subscribed to response channels: {Pattern}", pattern);
    }

    /// <summary>
    /// 엔티티 커맨드를 Redis Stream에 추가하고 결과 대기
    /// </summary>
    public async Task<TResult> EnqueueAsync<TResult>(
        IEntityCommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        await EnsureSubscribedAsync();

        var streamKey = GetStreamKey(command.EntityType, command.EntityId);
        var responseChannel = $"{_responseChannelPrefix}{command.CommandId}";

        // 응답 대기 등록
        var tcs = new TaskCompletionSource<RedisCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommands[command.CommandId.ToString()] = tcs;

        try
        {
            // 커맨드 직렬화
            var envelope = RedisCommandEnvelope.Create(command, responseChannel);
            var envelopeJson = JsonSerializer.Serialize(envelope);

            // Redis Stream에 추가 (XADD)
            var db = _connectionManager.GetDatabase();
            var messageId = await db.StreamAddAsync(
                streamKey,
                new NameValueEntry[]
                {
                    new("data", envelopeJson),
                    new("commandId", envelope.CommandId),
                    new("entityType", envelope.EntityType),
                    new("entityId", envelope.EntityId)
                },
                maxLength: 10000,  // 스트림 최대 길이 제한
                useApproximateMaxLength: true);

            _logger.LogDebug(
                "Command enqueued: CommandId={CommandId}, Stream={Stream}, MessageId={MessageId}",
                command.CommandId, streamKey, messageId);

            // 결과 대기 (타임아웃 적용)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_settings.CommandTimeoutSeconds));

            var timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"Command {command.CommandId} timed out after {_settings.CommandTimeoutSeconds} seconds");
            }

            var result = await tcs.Task;

            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "Command execution failed");
            }

            return result.DeserializeResult<TResult>()!;
        }
        finally
        {
            _pendingCommands.TryRemove(command.CommandId.ToString(), out _);
        }
    }

    /// <summary>
    /// 복합 커맨드 처리 (여러 엔티티 동시 잠금)
    /// Redis 분산 락 사용
    /// </summary>
    public async Task<TResult> EnqueueCompositeAsync<TResult>(
        ICommand<TResult> command,
        IEnumerable<(string EntityType, Guid EntityId)> entities,
        CancellationToken cancellationToken = default)
    {
        // 데드락 방지: 항상 같은 순서로 잠금
        var sortedEntities = entities
            .Select(e => $"{e.EntityType}:{e.EntityId}")
            .OrderBy(k => k)
            .ToList();

        _logger.LogDebug("Acquiring locks for composite command: {Entities}",
            string.Join(", ", sortedEntities));

        var locks = new List<IAsyncDisposable>();

        try
        {
            // 순서대로 분산 락 획득
            foreach (var entityKey in sortedEntities)
            {
                var lockHandle = await _distributedLock.AcquireAsync(entityKey, cancellationToken: cancellationToken);
                if (lockHandle == null)
                {
                    throw new InvalidOperationException($"Failed to acquire lock for {entityKey}");
                }
                locks.Add(lockHandle);
            }

            _logger.LogDebug("All locks acquired, executing composite command");

            // 여기서는 직접 실행 (이미 모든 락 보유)
            // 실제로는 핸들러를 직접 호출하거나 별도 처리 필요
            throw new NotImplementedException(
                "Composite command execution requires handler injection. " +
                "Use ICommandMediator.SendCompositeAsync for proper execution.");
        }
        finally
        {
            // 역순으로 락 해제
            for (int i = locks.Count - 1; i >= 0; i--)
            {
                await locks[i].DisposeAsync();
            }
        }
    }

    /// <summary>
    /// 스트림 키 생성
    /// </summary>
    internal string GetStreamKey(string entityType, Guid entityId)
    {
        return $"{_streamPrefix}{entityType}:{entityId}";
    }

    public void Dispose()
    {
        _subscriber?.UnsubscribeAll();

        // 대기 중인 커맨드 취소
        foreach (var kvp in _pendingCommands)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingCommands.Clear();
    }
}
