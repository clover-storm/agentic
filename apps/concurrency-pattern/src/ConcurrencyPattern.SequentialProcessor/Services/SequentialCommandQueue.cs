using System.Collections.Concurrent;
using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConcurrencyPattern.SequentialProcessor.Services;

/// <summary>
/// 순차 처리 큐 구현
/// 엔티티별로 독립적인 큐를 관리하여 동일 엔티티 요청만 순차 처리
/// 다른 엔티티에 대한 요청은 병렬 처리 가능
/// </summary>
public sealed class SequentialCommandQueue : ISequentialCommandQueue, IDisposable
{
    private readonly ConcurrentDictionary<string, EntityCommandQueue> _entityQueues = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _entityLocks = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SequentialCommandQueue> _logger;
    private readonly Timer _cleanupTimer;

    // 유휴 큐 정리 간격 (5분)
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    // 큐 유휴 시간 임계값 (10분)
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(10);

    public SequentialCommandQueue(
        IServiceProvider serviceProvider,
        ILogger<SequentialCommandQueue> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // 주기적으로 유휴 큐 정리
        _cleanupTimer = new Timer(CleanupIdleQueues, null, CleanupInterval, CleanupInterval);
    }

    /// <summary>
    /// 엔티티 커맨드를 해당 엔티티의 큐에 추가
    /// </summary>
    public async Task<TResult> EnqueueAsync<TResult>(
        IEntityCommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        var entityKey = GetEntityKey(command.EntityType, command.EntityId);

        _logger.LogDebug("Enqueueing command {CommandId} for entity {EntityKey}",
            command.CommandId, entityKey);

        var queue = GetOrCreateQueue(entityKey);

        return await queue.EnqueueAsync(
            command,
            ExecuteCommandAsync,
            cancellationToken);
    }

    /// <summary>
    /// 복합 커맨드 처리 (여러 엔티티 동시 잠금 필요)
    /// 데드락 방지를 위해 엔티티 키를 정렬하여 순서대로 잠금
    /// </summary>
    public async Task<TResult> EnqueueCompositeAsync<TResult>(
        ICommand<TResult> command,
        IEnumerable<(string EntityType, Guid EntityId)> entities,
        CancellationToken cancellationToken = default)
    {
        // 데드락 방지: 항상 같은 순서로 잠금 획득
        var sortedEntities = entities
            .Select(e => GetEntityKey(e.EntityType, e.EntityId))
            .OrderBy(k => k)
            .ToList();

        _logger.LogDebug("Enqueueing composite command {CommandId} for entities {Entities}",
            command.CommandId, string.Join(", ", sortedEntities));

        var locks = new List<SemaphoreSlim>();

        try
        {
            // 순서대로 잠금 획득
            foreach (var entityKey in sortedEntities)
            {
                var semaphore = _entityLocks.GetOrAdd(entityKey, _ => new SemaphoreSlim(1, 1));
                await semaphore.WaitAsync(cancellationToken);
                locks.Add(semaphore);
            }

            // 모든 잠금 획득 후 커맨드 실행
            return await ExecuteCompositeCommandAsync<TResult>(command, cancellationToken);
        }
        finally
        {
            // 역순으로 잠금 해제
            for (int i = locks.Count - 1; i >= 0; i--)
            {
                locks[i].Release();
            }
        }
    }

    /// <summary>
    /// 실제 커맨드 실행 - DI를 통해 핸들러 해결
    /// </summary>
    private async Task<TResult> ExecuteCommandAsync<TResult>(
        IEntityCommand<TResult> command,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var handleMethod = handlerType.GetMethod("HandleAsync");
        if (handleMethod == null)
            throw new InvalidOperationException($"Handler method not found for {command.GetType().Name}");

        var resultTask = (Task<TResult>)handleMethod.Invoke(handler, new object[] { command, cancellationToken })!;
        return await resultTask;
    }

    /// <summary>
    /// 복합 커맨드 실행
    /// </summary>
    private async Task<TResult> ExecuteCompositeCommandAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var handleMethod = handlerType.GetMethod("HandleAsync");
        if (handleMethod == null)
            throw new InvalidOperationException($"Handler method not found for {command.GetType().Name}");

        var resultTask = (Task<TResult>)handleMethod.Invoke(handler, new object[] { command, cancellationToken })!;
        return await resultTask;
    }

    /// <summary>
    /// 엔티티별 큐 가져오기 또는 생성
    /// </summary>
    private EntityCommandQueue GetOrCreateQueue(string entityKey)
    {
        return _entityQueues.GetOrAdd(entityKey, key =>
        {
            _logger.LogInformation("Creating new queue for entity {EntityKey}", key);
            return new EntityCommandQueue(key, _serviceProvider,
                _logger as ILogger ?? throw new InvalidOperationException());
        });
    }

    /// <summary>
    /// 엔티티 키 생성 (타입:ID 형식)
    /// </summary>
    private static string GetEntityKey(string entityType, Guid entityId)
    {
        return $"{entityType}:{entityId}";
    }

    /// <summary>
    /// 유휴 큐 정리
    /// </summary>
    private void CleanupIdleQueues(object? state)
    {
        var now = DateTime.UtcNow;
        var keysToRemove = new List<string>();

        foreach (var kvp in _entityQueues)
        {
            if (kvp.Value.IsIdle && (now - kvp.Value.LastAccessTime) > IdleThreshold)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            if (_entityQueues.TryRemove(key, out var queue))
            {
                _logger.LogInformation("Removed idle queue for entity {EntityKey}", key);
                queue.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();

        foreach (var queue in _entityQueues.Values)
        {
            queue.Dispose();
        }

        _entityQueues.Clear();
    }
}
