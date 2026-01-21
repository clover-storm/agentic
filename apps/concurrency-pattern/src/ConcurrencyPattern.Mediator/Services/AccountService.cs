using ConcurrencyPattern.Core.Commands;
using Microsoft.Extensions.Logging;

namespace ConcurrencyPattern.Mediator.Services;

/// <summary>
/// 계좌 서비스 - 비즈니스 로직 진입점
/// API 컨트롤러에서 호출하여 Mediator를 통해 커맨드 실행
/// </summary>
public interface IAccountService
{
    Task<AccountCommandResult> DepositAsync(Guid accountId, decimal amount, CancellationToken cancellationToken = default);
    Task<AccountCommandResult> WithdrawAsync(Guid accountId, decimal amount, CancellationToken cancellationToken = default);
    Task<TransferCommandResult> TransferAsync(Guid sourceAccountId, Guid targetAccountId, decimal amount, CancellationToken cancellationToken = default);
}

public class AccountService : IAccountService
{
    private readonly ICommandMediator _mediator;
    private readonly ILogger<AccountService> _logger;

    public AccountService(ICommandMediator mediator, ILogger<AccountService> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// 입금 처리
    /// 동일 계좌에 대한 동시 요청은 순차 처리됨
    /// </summary>
    public async Task<AccountCommandResult> DepositAsync(
        Guid accountId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deposit request: AccountId={AccountId}, Amount={Amount}",
            accountId, amount);

        var command = new DepositCommand
        {
            AccountId = accountId,
            Amount = amount
        };

        return await _mediator.SendAsync(command, cancellationToken);
    }

    /// <summary>
    /// 출금 처리
    /// 동일 계좌에 대한 동시 요청은 순차 처리됨
    /// </summary>
    public async Task<AccountCommandResult> WithdrawAsync(
        Guid accountId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Withdraw request: AccountId={AccountId}, Amount={Amount}",
            accountId, amount);

        var command = new WithdrawCommand
        {
            AccountId = accountId,
            Amount = amount
        };

        return await _mediator.SendAsync(command, cancellationToken);
    }

    /// <summary>
    /// 이체 처리
    /// 출금 계좌와 입금 계좌 모두에 대해 잠금을 획득한 후 실행
    /// </summary>
    public async Task<TransferCommandResult> TransferAsync(
        Guid sourceAccountId,
        Guid targetAccountId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Transfer request: SourceAccountId={SourceId}, TargetAccountId={TargetId}, Amount={Amount}",
            sourceAccountId, targetAccountId, amount);

        var command = new TransferCommand
        {
            SourceAccountId = sourceAccountId,
            TargetAccountId = targetAccountId,
            Amount = amount
        };

        // 두 계좌 모두에 대해 순차 처리 보장
        var entities = new[]
        {
            ("Account", sourceAccountId),
            ("Account", targetAccountId)
        };

        return await _mediator.SendCompositeAsync(command, entities, cancellationToken);
    }
}
