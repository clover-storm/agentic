using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Exceptions;
using ConcurrencyPattern.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ConcurrencyPattern.SequentialProcessor.Handlers;

/// <summary>
/// 입금 커맨드 핸들러
/// </summary>
public class DepositCommandHandler : ICommandHandler<DepositCommand, AccountCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DepositCommandHandler> _logger;

    public DepositCommandHandler(IUnitOfWork unitOfWork, ILogger<DepositCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AccountCommandResult> HandleAsync(
        DepositCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing deposit: AccountId={AccountId}, Amount={Amount}",
            command.AccountId, command.Amount);

        try
        {
            var account = await _unitOfWork.Accounts.GetByIdAsync(command.AccountId, cancellationToken);

            if (account == null)
            {
                return new AccountCommandResult
                {
                    Success = false,
                    ErrorMessage = $"계좌를 찾을 수 없습니다: {command.AccountId}"
                };
            }

            account.Deposit(command.Amount);
            await _unitOfWork.Accounts.UpdateAsync(account, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deposit completed: AccountId={AccountId}, NewBalance={Balance}",
                command.AccountId, account.Balance);

            return new AccountCommandResult
            {
                Success = true,
                NewBalance = account.Balance
            };
        }
        catch (ConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during deposit for AccountId={AccountId}",
                command.AccountId);

            return new AccountCommandResult
            {
                Success = false,
                ErrorMessage = "동시성 충돌이 발생했습니다. 다시 시도해주세요."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during deposit for AccountId={AccountId}", command.AccountId);

            return new AccountCommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// 출금 커맨드 핸들러
/// </summary>
public class WithdrawCommandHandler : ICommandHandler<WithdrawCommand, AccountCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WithdrawCommandHandler> _logger;

    public WithdrawCommandHandler(IUnitOfWork unitOfWork, ILogger<WithdrawCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AccountCommandResult> HandleAsync(
        WithdrawCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing withdrawal: AccountId={AccountId}, Amount={Amount}",
            command.AccountId, command.Amount);

        try
        {
            var account = await _unitOfWork.Accounts.GetByIdAsync(command.AccountId, cancellationToken);

            if (account == null)
            {
                return new AccountCommandResult
                {
                    Success = false,
                    ErrorMessage = $"계좌를 찾을 수 없습니다: {command.AccountId}"
                };
            }

            account.Withdraw(command.Amount);
            await _unitOfWork.Accounts.UpdateAsync(account, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Withdrawal completed: AccountId={AccountId}, NewBalance={Balance}",
                command.AccountId, account.Balance);

            return new AccountCommandResult
            {
                Success = true,
                NewBalance = account.Balance
            };
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("잔액 부족"))
        {
            _logger.LogWarning("Insufficient balance for AccountId={AccountId}", command.AccountId);

            return new AccountCommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (ConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during withdrawal for AccountId={AccountId}",
                command.AccountId);

            return new AccountCommandResult
            {
                Success = false,
                ErrorMessage = "동시성 충돌이 발생했습니다. 다시 시도해주세요."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during withdrawal for AccountId={AccountId}", command.AccountId);

            return new AccountCommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// 이체 커맨드 핸들러 - 두 계좌를 동시에 처리
/// </summary>
public class TransferCommandHandler : ICommandHandler<TransferCommand, TransferCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TransferCommandHandler> _logger;

    public TransferCommandHandler(IUnitOfWork unitOfWork, ILogger<TransferCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TransferCommandResult> HandleAsync(
        TransferCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Processing transfer: SourceAccountId={SourceId}, TargetAccountId={TargetId}, Amount={Amount}",
            command.SourceAccountId, command.TargetAccountId, command.Amount);

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var sourceAccount = await _unitOfWork.Accounts.GetByIdAsync(
                command.SourceAccountId, cancellationToken);
            var targetAccount = await _unitOfWork.Accounts.GetByIdAsync(
                command.TargetAccountId, cancellationToken);

            if (sourceAccount == null)
            {
                return new TransferCommandResult
                {
                    Success = false,
                    ErrorMessage = $"출금 계좌를 찾을 수 없습니다: {command.SourceAccountId}"
                };
            }

            if (targetAccount == null)
            {
                return new TransferCommandResult
                {
                    Success = false,
                    ErrorMessage = $"입금 계좌를 찾을 수 없습니다: {command.TargetAccountId}"
                };
            }

            sourceAccount.Transfer(targetAccount, command.Amount);

            await _unitOfWork.Accounts.UpdateAsync(sourceAccount, cancellationToken);
            await _unitOfWork.Accounts.UpdateAsync(targetAccount, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation(
                "Transfer completed: SourceBalance={SourceBalance}, TargetBalance={TargetBalance}",
                sourceAccount.Balance, targetAccount.Balance);

            return new TransferCommandResult
            {
                Success = true,
                SourceNewBalance = sourceAccount.Balance,
                TargetNewBalance = targetAccount.Balance
            };
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("잔액 부족"))
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);

            return new TransferCommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (ConcurrencyException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogWarning(ex, "Concurrency conflict during transfer");

            return new TransferCommandResult
            {
                Success = false,
                ErrorMessage = "동시성 충돌이 발생했습니다. 다시 시도해주세요."
            };
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Error during transfer");

            return new TransferCommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
