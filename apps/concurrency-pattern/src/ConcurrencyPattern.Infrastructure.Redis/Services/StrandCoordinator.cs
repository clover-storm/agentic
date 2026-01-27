using System.Collections.Concurrent;
using System.Text.Json;
using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// Strand 조정자 (Producer 측에서 사용)
///
/// 역할:
/// 1. 새 Strand 생성 시 Consumer들에게 Pub/Sub 알림
/// 2. Consumer 가용성 모니터링
/// </summary>
public interface IStrandCoordinator
{
    /// <summary>
    /// 새 Strand 등록 및 Consumer 알림
    /// Producer가 큐에 커맨드 추가 후 호출
    /// </summary>
    Task NotifyStrandActiveAsync(string entityType, string entityId);

    /// <summary>
    /// Strand 비활성화 알림 (큐가 완전히 비었을 때)
    /// </summary>
    Task NotifyStrandInactiveAsync(string entityType, string entityId);

    /// <summary>
    /// 현재 활성 Consumer 수 조회
    /// </summary>
    Task<int> GetActiveConsumerCountAsync();
}

public class StrandCoordinator : IStrandCoordinator
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly RedisSettings _settings;
    private readonly ILogger<StrandCoordinator> _logger;

    private readonly string _notificationChannel;
    private readonly string _consumerRegistryKey;

    public StrandCoordinator(
        IRedisConnectionManager connectionManager,
        IOptions<RedisSettings> settings,
        ILogger<StrandCoordinator> logger)
    {
        _connectionManager = connectionManager;
        _settings = settings.Value;
        _logger = logger;

        _notificationChannel = $"{_settings.InstanceName}:strand-notifications";
        _consumerRegistryKey = $"{_settings.InstanceName}:active-consumers";
    }

    public async Task NotifyStrandActiveAsync(string entityType, string entityId)
    {
        var notification = new StrandNotification
        {
            StrandKey = $"{entityType}:{entityId}",
            EventType = StrandEventType.Active,
            Timestamp = DateTimeOffset.UtcNow
        };

        var subscriber = _connectionManager.GetSubscriber();
        var message = JsonSerializer.Serialize(notification);
        await subscriber.PublishAsync(RedisChannel.Literal(_notificationChannel), message);

        _logger.LogDebug("Strand active notification published: {EntityType}:{EntityId}", entityType, entityId);
    }

    public async Task NotifyStrandInactiveAsync(string entityType, string entityId)
    {
        var notification = new StrandNotification
        {
            StrandKey = $"{entityType}:{entityId}",
            EventType = StrandEventType.Inactive,
            Timestamp = DateTimeOffset.UtcNow
        };

        var subscriber = _connectionManager.GetSubscriber();
        var message = JsonSerializer.Serialize(notification);
        await subscriber.PublishAsync(RedisChannel.Literal(_notificationChannel), message);

        _logger.LogDebug("Strand inactive notification published: {EntityType}:{EntityId}", entityType, entityId);
    }

    public async Task<int> GetActiveConsumerCountAsync()
    {
        var db = _connectionManager.GetDatabase();
        return (int)await db.SortedSetLengthAsync(_consumerRegistryKey);
    }
}

/// <summary>
/// Strand 알림 메시지 (Pub/Sub 전송)
/// </summary>
public class StrandNotification
{
    public string StrandKey { get; set; } = default!;
    public StrandEventType EventType { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public enum StrandEventType
{
    Active,
    Inactive
}

/// <summary>
/// Event-Driven Strand Consumer
///
/// 기본 StrandConsumer 확장 (Composition 방식):
/// - Polling + Pub/Sub 하이브리드 방식
/// - Pub/Sub으로 새 Strand 알림 수신 → 즉시 claim 시도
/// - Consumer Heartbeat로 가용성 등록
/// - 주기적 Discovery는 Pub/Sub 누락 방지용 백업
///
///   Producer                          EventDrivenStrandConsumer(N개)
///   ┌──────────┐                      ┌──────────────────────────┐
///   │ Enqueue  │──Pub/Sub 알림──────→│ 알림 수신 → 즉시 Claim   │
///   │ Command  │                      │ + 주기적 Discovery (백업) │
///   └──────────┘                      │ + Heartbeat (가용성)     │
///                                     └──────────────────────────┘
/// </summary>
public class EventDrivenStrandConsumer : BackgroundService
{
    private readonly StrandConsumer _innerConsumer;
    private readonly IRedisConnectionManager _connectionManager;
    private readonly RedisSettings _settings;
    private readonly ILogger<EventDrivenStrandConsumer> _logger;

    private readonly string _notificationChannel;
    private readonly string _consumerRegistryKey;
    private readonly string _consumerId;

    /// <summary>
    /// Pub/Sub으로 수신한 미처리 Strand 큐
    /// </summary>
    private readonly ConcurrentQueue<string> _pendingStrands = new();

    public EventDrivenStrandConsumer(
        IRedisConnectionManager connectionManager,
        IRedisDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        IOptions<RedisSettings> settings,
        ILogger<EventDrivenStrandConsumer> logger)
    {
        _connectionManager = connectionManager;
        _settings = settings.Value;
        _logger = logger;

        _notificationChannel = $"{_settings.InstanceName}:strand-notifications";
        _consumerRegistryKey = $"{_settings.InstanceName}:active-consumers";
        _consumerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

        // Composition: 내부 StrandConsumer 인스턴스
        _innerConsumer = new StrandConsumer(
            connectionManager, distributedLock, scopeFactory,
            settings, logger, _consumerId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventDrivenStrandConsumer starting: {ConsumerId}", _consumerId);

        // 1. Consumer 등록 (SortedSet)
        await RegisterConsumerAsync();

        // 2. Pub/Sub 구독 (Strand 알림)
        await SubscribeToNotificationsAsync();

        // 3. 병렬 Task 시작
        var heartbeatTask = HeartbeatLoopAsync(stoppingToken);
        var notificationTask = ProcessPendingStrandsAsync(stoppingToken);
        var discoveryTask = _innerConsumer.StartAsync(stoppingToken);

        // 모두 대기
        await Task.WhenAll(heartbeatTask, notificationTask, discoveryTask);

        // 4. 정리
        await UnregisterConsumerAsync();
        await _innerConsumer.StopAsync(CancellationToken.None);

        _logger.LogInformation("EventDrivenStrandConsumer stopped: {ConsumerId}", _consumerId);
    }

    /// <summary>
    /// Pub/Sub 알림으로 받은 Strand를 즉시 claim 시도
    /// </summary>
    private async Task ProcessPendingStrandsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                while (_pendingStrands.TryDequeue(out var strandKey))
                {
                    if (ct.IsCancellationRequested) break;
                    await _innerConsumer.TryClaimStrandAsync(strandKey, ct);
                }

                await Task.Delay(50, ct); // 50ms polling on pending queue
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing pending strands");
            }
        }
    }

    /// <summary>
    /// Strand 알림 Pub/Sub 구독
    /// </summary>
    private async Task SubscribeToNotificationsAsync()
    {
        var subscriber = _connectionManager.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Literal(_notificationChannel),
            (channel, message) =>
            {
                try
                {
                    var notification = JsonSerializer.Deserialize<StrandNotification>(message!);
                    if (notification?.EventType == StrandEventType.Active)
                    {
                        _pendingStrands.Enqueue(notification.StrandKey);
                        _logger.LogDebug("Strand notification received: {Strand}", notification.StrandKey);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error deserializing strand notification");
                }
            });

        _logger.LogInformation("Subscribed to strand notifications: {Channel}", _notificationChannel);
    }

    /// <summary>
    /// Consumer Heartbeat (SortedSet, score=timestamp)
    /// 60초 이상 heartbeat 없는 Consumer 자동 정리
    /// </summary>
    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        var db = _connectionManager.GetDatabase();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var score = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await db.SortedSetAddAsync(_consumerRegistryKey, _consumerId, score);

                // 오래된 Consumer 정리
                var cutoff = DateTimeOffset.UtcNow.AddSeconds(-60).ToUnixTimeSeconds();
                await db.SortedSetRemoveRangeByScoreAsync(_consumerRegistryKey, 0, cutoff);

                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat error for consumer: {ConsumerId}", _consumerId);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }

    private async Task RegisterConsumerAsync()
    {
        var db = _connectionManager.GetDatabase();
        var score = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await db.SortedSetAddAsync(_consumerRegistryKey, _consumerId, score);
        _logger.LogInformation("Consumer registered: {ConsumerId}", _consumerId);
    }

    private async Task UnregisterConsumerAsync()
    {
        try
        {
            var db = _connectionManager.GetDatabase();
            await db.SortedSetRemoveAsync(_consumerRegistryKey, _consumerId);
            _logger.LogInformation("Consumer unregistered: {ConsumerId}", _consumerId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error unregistering consumer: {ConsumerId}", _consumerId);
        }
    }
}
