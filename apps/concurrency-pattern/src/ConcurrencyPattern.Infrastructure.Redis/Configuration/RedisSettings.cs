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

    // ─── Strand 패턴 설정 ───

    /// <summary>
    /// Strand 모드 활성화
    /// true: StrandConsumer (N개 프로세스, 분산 Lock 기반)
    /// false: RedisContextConsumerManager (기존 단일 프로세스)
    /// </summary>
    public bool UseStrandMode { get; set; } = false;

    /// <summary>
    /// Event-Driven Strand Consumer 사용 여부
    /// true: Pub/Sub 알림 + Polling 하이브리드 (더 빠른 반응)
    /// false: Polling 기반 (더 안정적)
    /// </summary>
    public bool UseEventDrivenStrand { get; set; } = false;

    /// <summary>
    /// Strand Lock Lease 시간 (초)
    /// Consumer 장애 시 이 시간 후 다른 Consumer가 인계
    /// </summary>
    public int StrandLeaseSeconds { get; set; } = 30;

    /// <summary>
    /// Strand Batch Timeout (초)
    /// 이 시간마다 Lock TTL 갱신
    /// </summary>
    public int StrandBatchTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Strand 최대 Batch 크기
    /// 이 수만큼 처리 후 Lock TTL 갱신
    /// </summary>
    public int StrandMaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Strand Discovery 주기 (초)
    /// 새 Entity Queue를 발견하는 주기
    /// </summary>
    public int StrandDiscoveryIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// Strand Idle 최대 반복 횟수
    /// 큐가 비어있을 때 이 횟수 × 100ms 대기 후 Lock 해제
    /// </summary>
    public int StrandMaxIdleIterations { get; set; } = 100;
}
