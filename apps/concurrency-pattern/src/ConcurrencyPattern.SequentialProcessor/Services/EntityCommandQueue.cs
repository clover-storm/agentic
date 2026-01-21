using System.Threading.Channels;
using ConcurrencyPattern.Core.Commands;
using Microsoft.Extensions.Logging;

namespace ConcurrencyPattern.SequentialProcessor.Services;

/// <summary>
/// 단일 엔티티에 대한 커맨드 큐
/// Channel을 사용하여 해당 엔티티에 대한 요청을 순차 처리
/// </summary>
internal sealed class EntityCommandQueue : IDisposable
{
    private readonly Channel<CommandEnvelope> _channel;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cts;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly string _entityKey;

    public string EntityKey => _entityKey;
    public DateTime LastAccessTime { get; private set; } = DateTime.UtcNow;
    public bool IsIdle => _channel.Reader.Count == 0;

    public EntityCommandQueue(
        string entityKey,
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        _entityKey = entityKey;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _cts = new CancellationTokenSource();

        // Unbounded channel - 메모리 제한이 필요한 경우 Bounded로 변경
        _channel = Channel.CreateUnbounded<CommandEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = true,  // 단일 Consumer로 순차 처리 보장
            SingleWriter = false  // 여러 Producer 허용
        });

        // 백그라운드에서 큐 처리 시작
        _processingTask = ProcessQueueAsync(_cts.Token);
    }

    /// <summary>
    /// 커맨드를 큐에 추가
    /// </summary>
    public async Task<TResult> EnqueueAsync<TResult>(
        IEntityCommand<TResult> command,
        Func<IEntityCommand<TResult>, CancellationToken, Task<TResult>> handler,
        CancellationToken cancellationToken)
    {
        LastAccessTime = DateTime.UtcNow;

        var envelope = new CommandEnvelope<TResult>(command, handler);

        await _channel.Writer.WriteAsync(envelope, cancellationToken);

        // 결과 대기
        return await envelope.GetResultAsync(cancellationToken);
    }

    /// <summary>
    /// 큐에서 커맨드를 하나씩 처리 (단일 스레드)
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Entity queue started for {EntityKey}", _entityKey);

        await foreach (var envelope in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                _logger.LogDebug("Processing command {CommandId} for {EntityKey}",
                    envelope.CommandId, _entityKey);

                await envelope.ExecuteAsync(cancellationToken);

                _logger.LogDebug("Completed command {CommandId} for {EntityKey}",
                    envelope.CommandId, _entityKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command {CommandId} for {EntityKey}",
                    envelope.CommandId, _entityKey);
                // 개별 커맨드 실패는 큐 처리를 중단하지 않음
            }
        }

        _logger.LogInformation("Entity queue stopped for {EntityKey}", _entityKey);
    }

    public void Dispose()
    {
        _channel.Writer.Complete();
        _cts.Cancel();
        _cts.Dispose();
    }
}

/// <summary>
/// 커맨드 래퍼 - 결과 전달을 위한 TaskCompletionSource 포함
/// </summary>
internal abstract class CommandEnvelope
{
    public abstract Guid CommandId { get; }
    public abstract Task ExecuteAsync(CancellationToken cancellationToken);
}

internal sealed class CommandEnvelope<TResult> : CommandEnvelope
{
    private readonly IEntityCommand<TResult> _command;
    private readonly Func<IEntityCommand<TResult>, CancellationToken, Task<TResult>> _handler;
    private readonly TaskCompletionSource<TResult> _tcs;

    public override Guid CommandId => _command.CommandId;

    public CommandEnvelope(
        IEntityCommand<TResult> command,
        Func<IEntityCommand<TResult>, CancellationToken, Task<TResult>> handler)
    {
        _command = command;
        _handler = handler;
        _tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _handler(_command, cancellationToken);
            _tcs.TrySetResult(result);
        }
        catch (OperationCanceledException)
        {
            _tcs.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            _tcs.TrySetException(ex);
        }
    }

    public Task<TResult> GetResultAsync(CancellationToken cancellationToken)
    {
        // 취소 시 TaskCompletionSource도 취소 상태로 전환
        cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
        return _tcs.Task;
    }
}
