namespace ConcurrencyPattern.Core.Commands;

/// <summary>
/// 모든 커맨드의 기본 인터페이스
/// </summary>
public interface ICommand
{
    /// <summary>
    /// 커맨드 고유 식별자
    /// </summary>
    Guid CommandId { get; }

    /// <summary>
    /// 커맨드 생성 시간
    /// </summary>
    DateTime CreatedAt { get; }
}

/// <summary>
/// 결과를 반환하는 커맨드
/// </summary>
public interface ICommand<TResult> : ICommand
{
}

/// <summary>
/// 특정 엔티티에 대한 커맨드 (동시성 제어 대상)
/// </summary>
public interface IEntityCommand : ICommand
{
    /// <summary>
    /// 대상 엔티티 ID - 이 ID를 기준으로 순차 처리
    /// </summary>
    Guid EntityId { get; }

    /// <summary>
    /// 엔티티 타입 이름 - 같은 타입의 같은 ID만 순차 처리
    /// </summary>
    string EntityType { get; }
}

/// <summary>
/// 결과를 반환하는 엔티티 커맨드
/// </summary>
public interface IEntityCommand<TResult> : IEntityCommand, ICommand<TResult>
{
}
