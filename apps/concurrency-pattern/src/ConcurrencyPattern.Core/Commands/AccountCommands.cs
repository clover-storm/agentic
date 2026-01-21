namespace ConcurrencyPattern.Core.Commands;

/// <summary>
/// 계좌 커맨드 기본 클래스
/// </summary>
public abstract record AccountCommandBase : IEntityCommand<AccountCommandResult>
{
    public Guid CommandId { get; } = Guid.NewGuid();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public required Guid AccountId { get; init; }

    public Guid EntityId => AccountId;
    public string EntityType => "Account";
}

/// <summary>
/// 입금 커맨드
/// </summary>
public record DepositCommand : AccountCommandBase
{
    public required decimal Amount { get; init; }
}

/// <summary>
/// 출금 커맨드
/// </summary>
public record WithdrawCommand : AccountCommandBase
{
    public required decimal Amount { get; init; }
}

/// <summary>
/// 이체 커맨드 - 두 계좌를 동시에 잠금 필요
/// </summary>
public record TransferCommand : ICommand<TransferCommandResult>
{
    public Guid CommandId { get; } = Guid.NewGuid();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public required Guid SourceAccountId { get; init; }
    public required Guid TargetAccountId { get; init; }
    public required decimal Amount { get; init; }
}

/// <summary>
/// 계좌 커맨드 결과
/// </summary>
public record AccountCommandResult
{
    public bool Success { get; init; }
    public decimal NewBalance { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 이체 커맨드 결과
/// </summary>
public record TransferCommandResult
{
    public bool Success { get; init; }
    public decimal SourceNewBalance { get; init; }
    public decimal TargetNewBalance { get; init; }
    public string? ErrorMessage { get; init; }
}
