namespace ConcurrencyPattern.Api.DTOs;

/// <summary>
/// 입금 요청 DTO
/// </summary>
public record DepositRequest
{
    public decimal Amount { get; init; }
}

/// <summary>
/// 출금 요청 DTO
/// </summary>
public record WithdrawRequest
{
    public decimal Amount { get; init; }
}

/// <summary>
/// 이체 요청 DTO
/// </summary>
public record TransferRequest
{
    public Guid SourceAccountId { get; init; }
    public Guid TargetAccountId { get; init; }
    public decimal Amount { get; init; }
}

/// <summary>
/// 계좌 생성 요청 DTO
/// </summary>
public record CreateAccountRequest
{
    public string AccountNumber { get; init; } = string.Empty;
    public string HolderName { get; init; } = string.Empty;
    public decimal InitialBalance { get; init; }
}

/// <summary>
/// 계좌 응답 DTO
/// </summary>
public record AccountResponse
{
    public Guid Id { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string HolderName { get; init; } = string.Empty;
    public decimal Balance { get; init; }
}

/// <summary>
/// 작업 결과 응답 DTO
/// </summary>
public record OperationResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public object? Data { get; init; }
}
