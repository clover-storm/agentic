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
/// Redis List 기반 순차 처리 큐 (간소화 버전)
///
/// 핵심 원리:
/// - 엔티티별로 별도의 Redis List 생성
/// - Producer: LPUSH로 커맨드 추가
/// - Consumer: BRPOP으로 순차적으로 꺼내서 처리
/// - 같은 List에서 BRPOP하는 Consumer가 하나면 순차 처리 보장
/// - 다른 엔티티는 다른 List이므로 병렬 처리 가능
///
/// 분산 락 불필요 - BRPOP 자체가 원자적이고 순차적
/// </summary>
public class RedisListCommandQueue : ISequentialCommandQueue, IDisposable
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisListCommandQueue> _logger;

    private readonly string _queuePrefix;
    private readonly string _responseChannelPrefix;
    private readonly string _strandRegistryKey;

    // 응답 대기 중인 커맨드들
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RedisCommandResult>> _pendingCommands = new();

    private ISubscriber? _subscriber;
    private bool _isSubscribed;

    public RedisListCommandQueue(
        IRedisConnectionManager connectionManager,
        IOptions<RedisSettings> settings,
        ILogger<RedisListCommandQueue> logger)
    {
        _connectionManager = connectionManager;
        _settings = settings.Value;
        _logger = logger;

        _queuePrefix = $"{_settings.InstanceName}:queue:";
        _responseChannelPrefix = $"{_settings.InstanceName}:response:";
        _strandRegistryKey = $"{_settings.InstanceName}:active-strands";
    }

    /// <summary>
    /// 응답 채널 구독 시작
    /// </summary>
    private async Task EnsureSubscribedAsync()
    {
        if (_isSubscribed) return;

        _subscriber = _connectionManager.GetSubscriber();

        // 패턴 구독으로 모든 응답 채널 수신
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
    /// 엔티티 커맨드를 Redis List에 추가하고 결과 대기
    /// </summary>
    public async Task<TResult> EnqueueAsync<TResult>(
        IEntityCommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        await EnsureSubscribedAsync();

        var queueKey = GetQueueKey(command.EntityType, command.EntityId);
        var responseChannel = $"{_responseChannelPrefix}{command.CommandId}";

        // 응답 대기 등록
        var tcs = new TaskCompletionSource<RedisCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommands[command.CommandId.ToString()] = tcs;

        try
        {
            // 커맨드 직렬화
            var envelope = RedisCommandEnvelope.Create(command, responseChannel);
            var envelopeJson = JsonSerializer.Serialize(envelope);

            // Redis List에 LPUSH (왼쪽에 추가, RPOP은 오른쪽에서 꺼냄 = FIFO)
            var db = _connectionManager.GetDatabase();
            var strandKey = $"{command.EntityType}:{command.EntityId}";

            // LPUSH + SADD를 batch로 실행 (Strand Registry에 등록)
            var batch = db.CreateBatch();
            var pushTask = batch.ListLeftPushAsync(queueKey, envelopeJson);
            var registerTask = batch.SetAddAsync(_strandRegistryKey, strandKey);
            batch.Execute();
            await pushTask;
            await registerTask;

            _logger.LogDebug(
                "Command enqueued: CommandId={CommandId}, Queue={Queue}, Strand={Strand}",
                command.CommandId, queueKey, strandKey);

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
    /// 복합 커맨드 - 여러 엔티티에 대한 처리
    /// 이 경우에도 각 엔티티별 큐에 순차적으로 요청하면 됨
    /// (또는 별도의 "Transfer" 전용 큐 사용)
    /// </summary>
    public Task<TResult> EnqueueCompositeAsync<TResult>(
        ICommand<TResult> command,
        IEnumerable<(string EntityType, Guid EntityId)> entities,
        CancellationToken cancellationToken = default)
    {
        // 복합 커맨드는 별도의 전용 큐 또는 Saga 패턴으로 처리 권장
        // 여기서는 간단히 NotImplemented 처리
        throw new NotSupportedException(
            "복합 커맨드는 Saga 패턴 또는 전용 큐로 처리하세요. " +
            "단순 순차 처리에는 분산 락이 필요하지 않습니다.");
    }

    /// <summary>
    /// 큐 키 생성 (엔티티별로 별도 큐)
    /// </summary>
    public string GetQueueKey(string entityType, Guid entityId)
    {
        return $"{_queuePrefix}{entityType}:{entityId}";
    }

    public void Dispose()
    {
        _subscriber?.UnsubscribeAll();

        foreach (var kvp in _pendingCommands)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingCommands.Clear();
    }
}
