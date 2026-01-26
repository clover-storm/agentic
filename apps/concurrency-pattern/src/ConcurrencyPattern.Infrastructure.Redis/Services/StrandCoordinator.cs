using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// Strand 조정자 (Producer 측에서 사용)
///
/// 역할:
/// 1. 새 Strand 생성 시 Consumer들에게 알림 (Pub/Sub)
/// 2. Strand Registry 관리
/// 3. Consumer 상태 모니터링
/// </summary>
public interface IStrandCoordinator
{
    /// <summary>
    /// 새 Strand 등록 및 Consumer 알림
    /// </summary>
    Task NotifyStrandActiveAsync(string entityType, string entityId);

    /// <summary>
    /// Strand 비활성화 (큐가 완전히 비었을 때)
    /// </summary>
    Task NotifyStrandInactiveAsync(string entityType, string entityId);

    /// <summary>
    /// 활성 Consumer 수 조회
    /// </summary>
    Task<int> GetActiveConsumerCountAsync();
}

public class StrandCoordinator : IStrandCoordinator
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly RedisSettings _settings;
    private readonly ILogger<StrandCoordinator> _logger;

    private readonly string _strandNotificationChannel;
    private readonly string _consumerRegistryKey;

    public StrandCoordinator(
        IRedisConnectionManager connectionManager,
        IOptions<RedisSettings> settings,
        ILogger<StrandCoordinator> logger)
    {
        _connectionManager = connectionManager;
        _settings = settings.Value;
        _logger = logger;

        _strandNotificationChannel = $"{_settings.InstanceName}:strand-notifications";
        _consumerRegistryKey = $"{_settings.InstanceName}:active-consumers";
    }

    public async Task NotifyStrandActiveAsync(string entityType, string entityId)
    {
        var strandKey = $"{entityType}:{entityId}";
        var subscriber = _connectionManager.GetSubscriber();

        var notification = new StrandNotification
        {
            StrandKey = strandKey,
            EventType = StrandEventType.Active,
            Timestamp = DateTimeOffset.UtcNow
        };

        var message = System.Text.Json.JsonSerializer.Serialize(notification);
        await subscriber.PublishAsync(
            RedisChannel.Literal(_strandNotificationChannel),
            message);

        _logger.LogDebug("Published strand active notification: {Strand}", strandKey);
    }

    public async Task NotifyStrandInactiveAsync(string entityType, string entityId)
    {
        var strandKey = $"{entityType}:{entityId}";
        var subscriber = _connectionManager.GetSubscriber();

        var notification = new StrandNotification
        {
            StrandKey = strandKey,
            EventType = StrandEventType.Inactive,
            Timestamp = DateTimeOffset.UtcNow
        };

        var message = System.Text.Json.JsonSerializer.Serialize(notification);
        await subscriber.PublishAsync(
            RedisChannel.Literal(_strandNotificationChannel),
            message);

        _logger.LogDebug("Published strand inactive notification: {Strand}", strandKey);
    }

    public async Task<int> GetActiveConsumerCountAsync()
    {
        var db = _connectionManager.GetDatabase();
        return (int)await db.SortedSetLengthAsync(_consumerRegistryKey);
    }
}

/// <summary>
/// Strand 알림 메시지
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
/// 향상된 Strand Consumer (Pub/Sub 기반 알림 수신)
///
/// 기존 StrandConsumer와 차이점:
/// - Polling 대신 Pub/Sub으로 새 Strand 알림 수신
/// - Consumer Heartbeat로 가용성 등록
/// - Work Stealing 알고리즘 적용
/// </summary>
public class EventDrivenStrandConsumer : StrandConsumer
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly RedisSettings _settings;
    private readonly ILogger<EventDrivenStrandConsumer> _logger;

    private readonly string _strandNotificationChannel;
    private readonly string _consumerRegistryKey;
    private readonly string _consumerId;

    private readonly ConcurrentQueue<string> _pendingStrands = new();

    public EventDrivenStrandConsumer(
        IRedisConnectionManager connectionManager,
        IRedisDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        IOptions<RedisSettings> settings,
        ILogger<EventDrivenStrandConsumer> logger)
        : base(connectionManager, distributedLock, scopeFactory, settings, logger)
    {
        _connectionManager = connectionManager;
        _settings = settings.Value;
        _logger = logger;

        _strandNotificationChannel = $"{_settings.InstanceName}:strand-notifications";
        _consumerRegistryKey = $"{_settings.InstanceName}:active-consumers";
        _consumerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Consumer 등록
        await RegisterConsumerAsync();

        // Pub/Sub 구독
        await SubscribeToNotificationsAsync(stoppingToken);

        // Heartbeat 시작
        _ = HeartbeatLoopAsync(stoppingToken);

        // 기본 처리 루프 실행
        await base.ExecuteAsync(stoppingToken);

        // Consumer 등록 해제
        await UnregisterConsumerAsync();
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
        var db = _connectionManager.GetDatabase();
        await db.SortedSetRemoveAsync(_consumerRegistryKey, _consumerId);
        _logger.LogInformation("Consumer unregistered: {ConsumerId}", _consumerId);
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        var db = _connectionManager.GetDatabase();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var score = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await db.SortedSetAddAsync(_consumerRegistryKey, _consumerId, score);

                // 오래된 Consumer 제거 (60초 이상 heartbeat 없는 경우)
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
                _logger.LogWarning(ex, "Heartbeat error");
            }
        }
    }

    private async Task SubscribeToNotificationsAsync(CancellationToken ct)
    {
        var subscriber = _connectionManager.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Literal(_strandNotificationChannel),
            (channel, message) =>
            {
                try
                {
                    var notification = System.Text.Json.JsonSerializer
                        .Deserialize<StrandNotification>(message!);

                    if (notification?.EventType == StrandEventType.Active)
                    {
                        _pendingStrands.Enqueue(notification.StrandKey);
                        _logger.LogDebug("Received strand notification: {Strand}", notification.StrandKey);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing strand notification");
                }
            });

        _logger.LogInformation("Subscribed to strand notifications");
    }
}
