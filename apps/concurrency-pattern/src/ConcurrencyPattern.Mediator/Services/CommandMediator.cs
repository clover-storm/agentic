using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.SequentialProcessor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConcurrencyPattern.Mediator.Services;

/// <summary>
/// 커맨드 미디에이터 구현
/// 커맨드 타입에 따라 순차 처리 큐 또는 직접 실행으로 라우팅
/// </summary>
public class CommandMediator : ICommandMediator
{
    private readonly ISequentialCommandQueue _sequentialQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandMediator> _logger;

    public CommandMediator(
        ISequentialCommandQueue sequentialQueue,
        IServiceProvider serviceProvider,
        ILogger<CommandMediator> logger)
    {
        _sequentialQueue = sequentialQueue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// 엔티티 커맨드를 순차 처리 큐로 전송
    /// 동일 엔티티(타입+ID)에 대한 요청은 순차적으로 처리됨
    /// </summary>
    public async Task<TResult> SendAsync<TResult>(
        IEntityCommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Mediator received entity command: Type={CommandType}, EntityType={EntityType}, EntityId={EntityId}",
            command.GetType().Name, command.EntityType, command.EntityId);

        // 순차 처리 큐로 전달
        return await _sequentialQueue.EnqueueAsync(command, cancellationToken);
    }

    /// <summary>
    /// 복합 커맨드를 순차 처리 큐로 전송
    /// 지정된 모든 엔티티에 대해 잠금을 획득한 후 실행
    /// </summary>
    public async Task<TResult> SendCompositeAsync<TResult>(
        ICommand<TResult> command,
        IEnumerable<(string EntityType, Guid EntityId)> entities,
        CancellationToken cancellationToken = default)
    {
        var entityList = entities.ToList();

        _logger.LogInformation(
            "Mediator received composite command: Type={CommandType}, Entities={Entities}",
            command.GetType().Name,
            string.Join(", ", entityList.Select(e => $"{e.EntityType}:{e.EntityId}")));

        // 복합 커맨드는 여러 엔티티를 동시에 잠금
        return await _sequentialQueue.EnqueueCompositeAsync(command, entityList, cancellationToken);
    }

    /// <summary>
    /// 일반 커맨드 직접 실행 (순차 처리 없음)
    /// 조회 등 동시성 제어가 필요 없는 작업에 사용
    /// </summary>
    public async Task<TResult> SendDirectAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Mediator executing direct command: Type={CommandType}",
            command.GetType().Name);

        using var scope = _serviceProvider.CreateScope();
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var handleMethod = handlerType.GetMethod("HandleAsync");
        if (handleMethod == null)
            throw new InvalidOperationException($"Handler method not found for {command.GetType().Name}");

        var resultTask = (Task<TResult>)handleMethod.Invoke(handler, new object[] { command, cancellationToken })!;
        return await resultTask;
    }
}
