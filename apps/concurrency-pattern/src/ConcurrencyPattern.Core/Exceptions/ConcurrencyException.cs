namespace ConcurrencyPattern.Core.Exceptions;

/// <summary>
/// 동시성 충돌 예외
/// </summary>
public class ConcurrencyException : Exception
{
    public Guid EntityId { get; }
    public string EntityType { get; }

    public ConcurrencyException(string entityType, Guid entityId)
        : base($"동시성 충돌이 발생했습니다. 엔티티 타입: {entityType}, ID: {entityId}")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public ConcurrencyException(string entityType, Guid entityId, Exception innerException)
        : base($"동시성 충돌이 발생했습니다. 엔티티 타입: {entityType}, ID: {entityId}", innerException)
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}

/// <summary>
/// 엔티티를 찾을 수 없는 예외
/// </summary>
public class EntityNotFoundException : Exception
{
    public Guid EntityId { get; }
    public string EntityType { get; }

    public EntityNotFoundException(string entityType, Guid entityId)
        : base($"엔티티를 찾을 수 없습니다. 타입: {entityType}, ID: {entityId}")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
