namespace ConcurrencyPattern.Infrastructure.Redis.Configuration;

/// <summary>
/// Redis 설정
/// </summary>
public class RedisSettings
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Redis 연결 문자열
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// 인스턴스 이름 (키 접두사)
    /// </summary>
    public string InstanceName { get; set; } = "ConcurrencyPattern";

    /// <summary>
    /// 커맨드 응답 대기 타임아웃 (초)
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 분산 락 만료 시간 (초)
    /// </summary>
    public int LockExpirySeconds { get; set; } = 30;

    /// <summary>
    /// Consumer Group 이름
    /// </summary>
    public string ConsumerGroup { get; set; } = "command-processors";

    /// <summary>
    /// Consumer 이름 (인스턴스별 고유)
    /// </summary>
    public string ConsumerName { get; set; } = Environment.MachineName;
}
