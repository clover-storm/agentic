using ConcurrencyPattern.Core.Commands;

namespace ConcurrencyPattern.Core.Interfaces;

/// <summary>
/// 커맨드 핸들러 인터페이스
/// </summary>
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// 결과 없는 커맨드 핸들러
/// </summary>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
