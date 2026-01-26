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

    /// <summary>
    /// Strand 기반 분산 순차 처리 인프라 등록 (N개 Consumer 지원)
    ///
    /// Boost.Asio Strand 패턴 적용:
    /// ┌─────────────────────────────────────────────────────────────────┐
    /// │              Strand-based Distributed Processing                │
    /// ├─────────────────────────────────────────────────────────────────┤
    /// │                                                                 │
    /// │   Consumer 1         Consumer 2         Consumer 3              │
    /// │   ┌─────────┐        ┌─────────┐        ┌─────────┐            │
    /// │   │Lock: A:1│        │Lock: A:2│        │Lock: B:1│            │
    /// │   └────┬────┘        └────┬────┘        └────┬────┘            │
    /// │        ↓                  ↓                  ↓                  │
    /// │   Queue:A:1          Queue:A:2          Queue:B:1              │
    /// │   (순차처리)          (순차처리)          (순차처리)            │
    /// │                                                                 │
    /// │   ✅ Entity별 순차 보장  ✅ Entity간 병렬  ✅ N개 Consumer      │
    /// │   ✅ Consumer 장애 시 Lock TTL 만료 후 다른 Consumer 인계      │
    /// └─────────────────────────────────────────────────────────────────┘
    ///
    /// 특징:
    /// - 동일 Entity 내: 순차 처리 보장 (분산 Lock)
    /// - 다른 Entity 간: 완전 병렬 처리
    /// - Single Point of Failure 제거 (N개 Consumer 가능)
    /// - Consumer 장애 시 자동 인계 (Lock TTL)
    /// </summary>
    public static IServiceCollection AddRedisStrandInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RedisSettings>(
            configuration.GetSection(RedisSettings.SectionName));

        // 핵심 인프라
        services.AddSingleton<IRedisConnectionManager, RedisConnectionManager>();
        services.AddSingleton<IRedisDistributedLock, RedisDistributedLock>();

        // Strand 조정자 (Producer 측)
        services.AddSingleton<IStrandCoordinator, StrandCoordinator>();

        // Producer 큐 (기존 List 기반 재사용)
        services.AddSingleton<ISequentialCommandQueue, RedisListCommandQueue>();

        // Strand Consumer (분산 Lock 기반)
        services.AddHostedService<StrandConsumer>();

        return services;
    }

    /// <summary>
    /// Event-Driven Strand Consumer 등록 (Pub/Sub 기반 알림)
    ///
    /// 기본 Strand Consumer와 차이:
    /// - Polling 대신 Pub/Sub으로 새 Strand 알림 수신
    /// - Consumer Heartbeat로 가용성 등록
    /// - 더 빠른 반응 시간
    /// </summary>
    public static IServiceCollection AddRedisEventDrivenStrandInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RedisSettings>(
            configuration.GetSection(RedisSettings.SectionName));

        services.AddSingleton<IRedisConnectionManager, RedisConnectionManager>();
        services.AddSingleton<IRedisDistributedLock, RedisDistributedLock>();
        services.AddSingleton<IStrandCoordinator, StrandCoordinator>();
        services.AddSingleton<ISequentialCommandQueue, RedisListCommandQueue>();

        // Event-Driven Consumer (Pub/Sub 기반)
        services.AddHostedService<EventDrivenStrandConsumer>();

        return services;
    }
}
