using System.Text.Json;
using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// Redis Streams Consumer Worker
///
/// 엔티티별 스트림에서 커맨드를 읽어 순차 처리하고 결과를 Pub/Sub으로 발행
/// Consumer Group을 사용하여 여러 인스턴스에서 분산 처리 가능
/// </summary>
public class RedisCommandConsumerWorker : BackgroundService
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisCommandConsumerWorker> _logger;

    private readonly string _streamPrefix;
    private readonly HashSet<string> _processingStreams = new();
    private readonly SemaphoreSlim _streamDiscoveryLock = new(1, 1);

    public RedisCommandConsumerWorker(
        IRedisConnectionManager connectionManager,
        IServiceProvider serviceProvider,
        IOptions<RedisSettings> settings,
        ILogger<RedisCommandConsumerWorker> logger)
    {
        _connectionManager = connectionManager;
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
        _streamPrefix = $"{_settings.InstanceName}:stream:";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Redis Command Consumer Worker starting...");

        // 기존 스트림 검색 및 Consumer Group 생성
        await DiscoverAndSetupStreamsAsync(stoppingToken);

        // 주기적으로 새 스트림 검색
        var discoveryTask = PeriodicStreamDiscoveryAsync(stoppingToken);

        // 스트림 처리 시작
        var processingTask = ProcessStreamsAsync(stoppingToken);

        await Task.WhenAll(discoveryTask, processingTask);
    }

    /// <summary>
    /// 기존 스트림 검색 및 Consumer Group 설정
    /// </summary>
    private async Task DiscoverAndSetupStreamsAsync(CancellationToken ct)
    {
        try
        {
            var server = _connectionManager.GetServer();
            var pattern = $"{_streamPrefix}*";

            await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(ct))
            {
                await EnsureConsumerGroupAsync(key.ToString());
                _processingStreams.Add(key.ToString());
            }

            _logger.LogInformation("Discovered {Count} existing streams", _processingStreams.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering existing streams");
        }
    }

    /// <summary>
    /// 주기적으로 새 스트림 검색
    /// </summary>
    private async Task PeriodicStreamDiscoveryAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(10);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);

            try
            {
                await _streamDiscoveryLock.WaitAsync(ct);
                try
                {
                    await DiscoverAndSetupStreamsAsync(ct);
                }
                finally
                {
                    _streamDiscoveryLock.Release();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in periodic stream discovery");
            }
        }
    }

    /// <summary>
    /// Consumer Group 생성 (없으면)
    /// </summary>
    private async Task EnsureConsumerGroupAsync(string streamKey)
    {
        var db = _connectionManager.GetDatabase();

        try
        {
            // Consumer Group 생성 시도 (이미 있으면 예외)
            await db.StreamCreateConsumerGroupAsync(
                streamKey,
                _settings.ConsumerGroup,
                StreamPosition.NewMessages);

            _logger.LogDebug("Created consumer group for stream: {Stream}", streamKey);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // 이미 존재하는 경우 무시
            _logger.LogDebug("Consumer group already exists for stream: {Stream}", streamKey);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("no such key"))
        {
            // 스트림이 아직 없는 경우 (첫 메시지가 오면 자동 생성됨)
            _logger.LogDebug("Stream does not exist yet: {Stream}", streamKey);
        }
    }

    /// <summary>
    /// 모든 스트림에서 커맨드 읽어 처리
    /// </summary>
    private async Task ProcessStreamsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var streams = _processingStreams.ToArray();

            if (streams.Length == 0)
            {
                await Task.Delay(1000, ct);
                continue;
            }

            foreach (var streamKey in streams)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await ProcessStreamAsync(streamKey, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing stream: {Stream}", streamKey);
                }
            }

            // 짧은 대기 후 다음 라운드
            await Task.Delay(100, ct);
        }
    }

    /// <summary>
    /// 단일 스트림 처리
    /// </summary>
    private async Task ProcessStreamAsync(string streamKey, CancellationToken ct)
    {
        var db = _connectionManager.GetDatabase();

        // Consumer Group에서 새 메시지 읽기
        var entries = await db.StreamReadGroupAsync(
            streamKey,
            _settings.ConsumerGroup,
            _settings.ConsumerName,
            ">",  // 새 메시지만
            count: 1);  // 한 번에 하나씩 (순차 처리)

        if (entries == null || entries.Length == 0)
            return;

        foreach (var entry in entries)
        {
            try
            {
                var dataEntry = entry.Values.FirstOrDefault(v => v.Name == "data");
                if (dataEntry.Value.IsNullOrEmpty)
                {
                    _logger.LogWarning("Empty data in stream entry: {MessageId}", entry.Id);
                    continue;
                }

                var envelope = JsonSerializer.Deserialize<RedisCommandEnvelope>(dataEntry.Value!);
                if (envelope == null)
                {
                    _logger.LogWarning("Failed to deserialize envelope: {MessageId}", entry.Id);
                    continue;
                }

                _logger.LogDebug("Processing command: {CommandId} from {Stream}",
                    envelope.CommandId, streamKey);

                // 커맨드 실행
                var result = await ExecuteCommandAsync(envelope, ct);

                // 결과 발행 (Pub/Sub)
                await PublishResultAsync(envelope.ResponseChannel, result);

                // 메시지 ACK
                await db.StreamAcknowledgeAsync(streamKey, _settings.ConsumerGroup, entry.Id);

                _logger.LogDebug("Command completed: {CommandId}", envelope.CommandId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing stream entry: {MessageId}", entry.Id);

                // 실패한 경우에도 ACK (재시도 로직은 별도 구현 필요)
                await db.StreamAcknowledgeAsync(streamKey, _settings.ConsumerGroup, entry.Id);
            }
        }
    }

    /// <summary>
    /// 커맨드 실행
    /// </summary>
    private async Task<RedisCommandResult> ExecuteCommandAsync(
        RedisCommandEnvelope envelope,
        CancellationToken ct)
    {
        try
        {
            var command = envelope.DeserializeCommand();

            using var scope = _serviceProvider.CreateScope();

            // 핸들러 타입 결정
            var commandType = Type.GetType(envelope.CommandType)
                ?? throw new InvalidOperationException($"Cannot resolve command type: {envelope.CommandType}");
            var resultType = Type.GetType(envelope.ResultType)
                ?? throw new InvalidOperationException($"Cannot resolve result type: {envelope.ResultType}");

            var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, resultType);
            var handler = scope.ServiceProvider.GetRequiredService(handlerType);

            var handleMethod = handlerType.GetMethod("HandleAsync")
                ?? throw new InvalidOperationException("HandleAsync method not found");

            var resultTask = (Task)handleMethod.Invoke(handler, new object[] { command, ct })!;
            await resultTask;

            // Task<TResult>에서 Result 추출
            var resultProperty = resultTask.GetType().GetProperty("Result");
            var result = resultProperty?.GetValue(resultTask);

            return new RedisCommandResult
            {
                CommandId = envelope.CommandId,
                Success = true,
                ResultType = envelope.ResultType,
                ResultData = JsonSerializer.Serialize(result)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {CommandId}", envelope.CommandId);
            return RedisCommandResult.FromError(envelope.CommandId, ex);
        }
    }

    /// <summary>
    /// 결과를 Pub/Sub으로 발행
    /// </summary>
    private async Task PublishResultAsync(string channel, RedisCommandResult result)
    {
        try
        {
            var subscriber = _connectionManager.GetSubscriber();
            var resultJson = JsonSerializer.Serialize(result);

            await subscriber.PublishAsync(
                RedisChannel.Literal(channel),
                resultJson);

            _logger.LogDebug("Result published to channel: {Channel}", channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing result to channel: {Channel}", channel);
        }
    }
}
