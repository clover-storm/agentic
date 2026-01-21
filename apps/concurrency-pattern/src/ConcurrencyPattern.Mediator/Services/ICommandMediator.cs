using ConcurrencyPattern.Core.Commands;

namespace ConcurrencyPattern.Mediator.Services;

/// <summary>
/// 커맨드 미디에이터 인터페이스
/// 모든 커맨드 요청의 진입점으로, 순차 처리가 필요한 커맨드를 적절히 라우팅
/// </summary>
public interface ICommandMediator
{
    /// <summary>
    /// 엔티티 커맨드 전송 - 동일 엔티티에 대한 요청은 순차 처리
    /// </summary>
    Task<TResult> SendAsync<TResult>(IEntityCommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 복합 커맨드 전송 - 여러 엔티티를 동시에 잠금
    /// </summary>
    Task<TResult> SendCompositeAsync<TResult>(
        ICommand<TResult> command,
        IEnumerable<(string EntityType, Guid EntityId)> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 일반 커맨드 전송 - 순차 처리 없이 바로 실행
    /// </summary>
    Task<TResult> SendDirectAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);
}
