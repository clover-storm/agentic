using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using ConcurrencyPattern.Infrastructure.Redis.Services;
using ConcurrencyPattern.SequentialProcessor.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ConcurrencyPattern.Infrastructure.Redis;

/// <summary>
/// Redis 인프라스트럭처 DI 확장
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Redis List 기반 순차 처리 인프라 등록 (권장)
    ///
    /// 구조:
    /// ┌─────────────────────────────────────────────────────────────┐
    /// │  컨텍스트별 완전 독립 처리                                    │
    /// │                                                              │
    /// │  Account Context        Inventory Context                   │
    /// │  ┌─────────────────┐   ┌─────────────────┐                  │
    /// │  │ queue:Account:1 │   │ queue:Inventory:A│  ← 병렬 처리    │
    /// │  │ queue:Account:2 │   │ queue:Inventory:B│  ← 병렬 처리    │
    /// │  │  (엔티티별 순차)│   │  (엔티티별 순차) │                  │
    /// │  └─────────────────┘   └─────────────────┘                  │
    /// │         ↑                      ↑                            │
    /// │    AccountConsumer       InventoryConsumer                  │
    /// │      (독립 Task)          (독립 Task)                       │
    /// └─────────────────────────────────────────────────────────────┘
    ///
    /// 핵심:
    /// - 컨텍스트(Account, Inventory) 간: 완전 병렬
    /// - 컨텍스트 내 엔티티(Account:1, Account:2) 간: 병렬
    /// - 동일 엔티티(Account:1) 내 요청: 순차
    /// </summary>
    public static IServiceCollection AddRedisInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 설정 바인딩
        services.Configure<RedisSettings>(
            configuration.GetSection(RedisSettings.SectionName));

        // Redis 연결 관리자 (Singleton)
        services.AddSingleton<IRedisConnectionManager, RedisConnectionManager>();

        // Redis List 기반 순차 처리 큐 (Producer)
        services.AddSingleton<ISequentialCommandQueue, RedisListCommandQueue>();

        // 컨텍스트별 Consumer 관리자 (Hosted Service)
        services.AddHostedService<RedisContextConsumerManager>();

        return services;
    }

    /// <summary>
    /// Redis 기반 순차 처리 인프라 등록 (연결 문자열 직접 지정)
    /// </summary>
    public static IServiceCollection AddRedisInfrastructure(
        this IServiceCollection services,
        string connectionString,
        Action<RedisSettings>? configure = null)
    {
        services.Configure<RedisSettings>(options =>
        {
            options.ConnectionString = connectionString;
            configure?.Invoke(options);
        });

        services.AddSingleton<IRedisConnectionManager, RedisConnectionManager>();
        services.AddSingleton<ISequentialCommandQueue, RedisListCommandQueue>();
        services.AddHostedService<RedisContextConsumerManager>();

        return services;
    }

    /// <summary>
    /// Redis Streams 기반 순차 처리 인프라 등록 (복잡한 경우)
    ///
    /// Streams 사용 시 장점:
    /// - Consumer Group으로 여러 Consumer 관리
    /// - 메시지 ACK 및 재처리 지원
    /// - 메시지 이력 보존
    /// </summary>
    public static IServiceCollection AddRedisStreamsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RedisSettings>(
            configuration.GetSection(RedisSettings.SectionName));

        services.AddSingleton<IRedisConnectionManager, RedisConnectionManager>();
        services.AddSingleton<IRedisDistributedLock, RedisDistributedLock>();
        services.AddSingleton<ISequentialCommandQueue, RedisSequentialCommandQueue>();
        services.AddHostedService<RedisCommandConsumerWorker>();

        return services;
    }
}
