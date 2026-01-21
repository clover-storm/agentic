using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// 컨텍스트별 Consumer 관리자
///
/// 구조:
/// ┌─────────────────────────────────────────────────────────────────┐
/// │                   ConsumerManager                                │
/// │  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐ │
/// │  │ AccountConsumer │  │InventoryConsumer│  │  OrderConsumer  │ │
/// │  │  (독립 Task)    │  │  (독립 Task)    │  │  (독립 Task)    │ │
/// │  │  ┌───────────┐  │  │  ┌───────────┐  │  │  ┌───────────┐  │ │
/// │  │  │Entity:1   │  │  │  │Entity:A   │  │  │  │Entity:X   │  │ │
/// │  │  │(순차처리) │  │  │  │(순차처리) │  │  │  │(순차처리) │  │ │
/// │  │  ├───────────┤  │  │  ├───────────┤  │  │  ├───────────┤  │ │
/// │  │  │Entity:2   │  │  │  │Entity:B   │  │  │  │Entity:Y   │  │ │
/// │  │  │(병렬처리) │  │  │  │(병렬처리) │  │  │  │(병렬처리) │  │ │
/// │  │  └───────────┘  │  │  └───────────┘  │  │  └───────────┘  │ │
/// │  └─────────────────┘  └─────────────────┘  └─────────────────┘ │
/// │        ↕ 완전 병렬        ↕ 완전 병렬        ↕ 완전 병렬       │
/// └─────────────────────────────────────────────────────────────────┘
/// </summary>
public class RedisContextConsumerManager : BackgroundService
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisContextConsumerManager> _logger;

    private readonly Dictionary<string, RedisContextConsumer> _contextConsumers = new();
    private readonly SemaphoreSlim _consumerLock = new(1, 1);
    private readonly string _queuePrefix;

    public RedisContextConsumerManager(
        IRedisConnectionManager connectionManager,
        IServiceProvider serviceProvider,
        IOptions<RedisSettings> settings,
        ILogger<RedisContextConsumerManager> logger)
    {
        _connectionManager = connectionManager;
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
        _queuePrefix = $"{_settings.InstanceName}:queue:";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Redis Context Consumer Manager starting...");

        // 주기적으로 새 컨텍스트 검색 및 Consumer 시작
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DiscoverAndStartContextConsumersAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in context discovery");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        // 정리
        await StopAllConsumersAsync();
        _logger.LogInformation("Redis Context Consumer Manager stopped.");
    }

    /// <summary>
    /// 컨텍스트 검색 및 Consumer 시작
    /// </summary>
    private async Task DiscoverAndStartContextConsumersAsync(CancellationToken ct)
    {
        var server = _connectionManager.GetServer();
        var pattern = $"{_queuePrefix}*";
        var discoveredContexts = new HashSet<string>();

        // 모든 큐 키에서 컨텍스트 추출
        await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(ct))
        {
            var keyStr = key.ToString();
            var contextName = ExtractContextName(keyStr);
            if (!string.IsNullOrEmpty(contextName))
            {
                discoveredContexts.Add(contextName);
            }
        }

        // 새 컨텍스트에 대한 Consumer 시작
        await _consumerLock.WaitAsync(ct);
        try
        {
            foreach (var contextName in discoveredContexts)
            {
                if (!_contextConsumers.ContainsKey(contextName))
                {
                    await StartContextConsumerAsync(contextName, ct);
                }
            }
        }
        finally
        {
            _consumerLock.Release();
        }
    }

    /// <summary>
    /// 컨텍스트 이름 추출 (queue:Account:123 → Account)
    /// </summary>
    private string? ExtractContextName(string queueKey)
    {
        // 형식: {prefix}:queue:{ContextName}:{EntityId}
        var prefixLength = _queuePrefix.Length;
        if (queueKey.Length <= prefixLength)
            return null;

        var remainder = queueKey.Substring(prefixLength);
        var colonIndex = remainder.IndexOf(':');
        if (colonIndex < 0)
            return remainder;

        return remainder.Substring(0, colonIndex);
    }

    /// <summary>
    /// 새 컨텍스트 Consumer 시작
    /// </summary>
    private async Task StartContextConsumerAsync(string contextName, CancellationToken ct)
    {
        _logger.LogInformation("Starting context consumer: {Context}", contextName);

        var consumer = new RedisContextConsumer(
            contextName,
            _connectionManager,
            _serviceProvider,
            _serviceProvider.GetRequiredService<IOptions<RedisSettings>>(),
            _logger);

        _contextConsumers[contextName] = consumer;

        // 백그라운드에서 Consumer 실행
        _ = Task.Run(() => consumer.StartAsync(ct), ct);
    }

    /// <summary>
    /// 모든 Consumer 정지
    /// </summary>
    private async Task StopAllConsumersAsync()
    {
        await _consumerLock.WaitAsync();
        try
        {
            foreach (var consumer in _contextConsumers.Values)
            {
                consumer.Dispose();
            }
            _contextConsumers.Clear();
        }
        finally
        {
            _consumerLock.Release();
        }
    }

    public override void Dispose()
    {
        foreach (var consumer in _contextConsumers.Values)
        {
            consumer.Dispose();
        }
        _consumerLock.Dispose();
        base.Dispose();
    }
}
