using System.Text.Json;
using ConcurrencyPattern.Core.Commands;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// Redis로 전송되는 커맨드 래퍼
/// 직렬화/역직렬화 및 메타데이터 포함
/// </summary>
public class RedisCommandEnvelope
{
    /// <summary>
    /// 커맨드 고유 ID
    /// </summary>
    public string CommandId { get; set; } = string.Empty;

    /// <summary>
    /// 커맨드 타입 전체 이름 (역직렬화용)
    /// </summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// 결과 타입 전체 이름
    /// </summary>
    public string ResultType { get; set; } = string.Empty;

    /// <summary>
    /// 직렬화된 커맨드 데이터
    /// </summary>
    public string CommandData { get; set; } = string.Empty;

    /// <summary>
    /// 엔티티 타입
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// 엔티티 ID
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// 응답을 받을 Pub/Sub 채널
    /// </summary>
    public string ResponseChannel { get; set; } = string.Empty;

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 타임아웃 (밀리초)
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    public static RedisCommandEnvelope Create<TResult>(
        IEntityCommand<TResult> command,
        string responseChannel)
    {
        return new RedisCommandEnvelope
        {
            CommandId = command.CommandId.ToString(),
            CommandType = command.GetType().AssemblyQualifiedName ?? command.GetType().FullName!,
            ResultType = typeof(TResult).AssemblyQualifiedName ?? typeof(TResult).FullName!,
            CommandData = JsonSerializer.Serialize(command, command.GetType()),
            EntityType = command.EntityType,
            EntityId = command.EntityId.ToString(),
            ResponseChannel = responseChannel,
            CreatedAt = DateTime.UtcNow
        };
    }

    public ICommand DeserializeCommand()
    {
        var type = Type.GetType(CommandType)
            ?? throw new InvalidOperationException($"Cannot resolve command type: {CommandType}");

        return (ICommand)(JsonSerializer.Deserialize(CommandData, type)
            ?? throw new InvalidOperationException("Failed to deserialize command"));
    }
}

/// <summary>
/// 커맨드 실행 결과
/// </summary>
public class RedisCommandResult
{
    /// <summary>
    /// 원본 커맨드 ID
    /// </summary>
    public string CommandId { get; set; } = string.Empty;

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 결과 타입
    /// </summary>
    public string ResultType { get; set; } = string.Empty;

    /// <summary>
    /// 직렬화된 결과 데이터
    /// </summary>
    public string? ResultData { get; set; }

    /// <summary>
    /// 에러 메시지 (실패 시)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 예외 타입 (실패 시)
    /// </summary>
    public string? ExceptionType { get; set; }

    public static RedisCommandResult FromSuccess<TResult>(string commandId, TResult result)
    {
        return new RedisCommandResult
        {
            CommandId = commandId,
            Success = true,
            ResultType = typeof(TResult).AssemblyQualifiedName ?? typeof(TResult).FullName!,
            ResultData = JsonSerializer.Serialize(result)
        };
    }

    public static RedisCommandResult FromError(string commandId, Exception ex)
    {
        return new RedisCommandResult
        {
            CommandId = commandId,
            Success = false,
            ErrorMessage = ex.Message,
            ExceptionType = ex.GetType().FullName
        };
    }

    public TResult? DeserializeResult<TResult>()
    {
        if (!Success || string.IsNullOrEmpty(ResultData))
            return default;

        return JsonSerializer.Deserialize<TResult>(ResultData);
    }
}
