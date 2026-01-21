namespace ConcurrencyPattern.Core.Entities;

/// <summary>
/// 모든 엔티티의 기본 클래스
/// RowVersion을 통한 낙관적 동시성 제어 지원
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// EF Core의 Optimistic Concurrency를 위한 RowVersion
    /// 데이터베이스에서 자동으로 관리됨
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
