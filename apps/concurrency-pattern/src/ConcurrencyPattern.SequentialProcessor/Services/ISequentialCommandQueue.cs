using ConcurrencyPattern.Core.Commands;

namespace ConcurrencyPattern.SequentialProcessor.Services;

/// <summary>
/// 순차 처리 큐 인터페이스
/// 동일 엔티티에 대한 요청을 순차적으로 처리
/// </summary>
public interface ISequentialCommandQueue
{
    /// <summary>
    /// 커맨드를 큐에 추가하고 결과를 대기
    /// </summary>
    Task<TResult> EnqueueAsync<TResult>(IEntityCommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 복합 커맨드 (여러 엔티티 동시 잠금) 처리
    /// </summary>
    Task<TResult> EnqueueCompositeAsync<TResult>(
        ICommand<TResult> command,
        IEnumerable<(string EntityType, Guid EntityId)> entities,
        CancellationToken cancellationToken = default);
}
