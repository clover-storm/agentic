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
    /// Redis 기반 순차 처리 인프라 등록
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

        // 분산 락 (Singleton)
        services.AddSingleton<IRedisDistributedLock, RedisDistributedLock>();

        // Redis 기반 순차 처리 큐 (Singleton)
        // 기존 ISequentialCommandQueue를 Redis 버전으로 대체
        services.AddSingleton<ISequentialCommandQueue, RedisSequentialCommandQueue>();

        // Consumer Worker (Hosted Service)
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
        services.AddSingleton<IRedisDistributedLock, RedisDistributedLock>();
        services.AddSingleton<ISequentialCommandQueue, RedisSequentialCommandQueue>();
        services.AddHostedService<RedisCommandConsumerWorker>();

        return services;
    }
}
