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
    /// Redis List 기반 순차 처리 인프라 등록 (권장, 간소화 버전)
    ///
    /// 동작 원리:
    /// - 엔티티별 Redis List 생성 (queue:Account:123)
    /// - Producer: LPUSH로 커맨드 추가
    /// - Consumer: RPOP/BRPOP으로 순차적으로 꺼내서 처리
    /// - 분산 락 불필요 - List 자체가 순차 처리 보장
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

        // Redis List 기반 순차 처리 큐 (간소화)
        services.AddSingleton<ISequentialCommandQueue, RedisListCommandQueue>();

        // Consumer Worker (Hosted Service)
        services.AddHostedService<RedisListConsumerWorker>();

        return services;
    }

    /// <summary>
    /// Redis Streams 기반 순차 처리 인프라 등록
    ///
    /// Streams 사용 시 장점:
    /// - Consumer Group으로 여러 Consumer 관리
    /// - 메시지 ACK 및 재처리 지원
    /// - 메시지 이력 보존
    ///
    /// 단점:
    /// - List보다 복잡
    /// - 단순 순차 처리에는 과도할 수 있음
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
        services.AddHostedService<RedisListConsumerWorker>();

        return services;
    }
}
