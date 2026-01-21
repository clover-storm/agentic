using ConcurrencyPattern.Api.DTOs;
using ConcurrencyPattern.Core.Entities;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Mediator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ConcurrencyPattern.Api.Controllers;

/// <summary>
/// 계좌 관련 API 컨트롤러
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly IAccountService _accountService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(
        IAccountService accountService,
        IUnitOfWork unitOfWork,
        ILogger<AccountsController> logger)
    {
        _accountService = accountService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// 계좌 목록 조회
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AccountResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var accounts = await _unitOfWork.Accounts.GetAllAsync(cancellationToken);
        return Ok(accounts.Select(a => new AccountResponse
        {
            Id = a.Id,
            AccountNumber = a.AccountNumber,
            HolderName = a.HolderName,
            Balance = a.Balance
        }));
    }

    /// <summary>
    /// 계좌 상세 조회
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetByIdAsync(id, cancellationToken);
        if (account == null)
            return NotFound();

        return Ok(new AccountResponse
        {
            Id = account.Id,
            AccountNumber = account.AccountNumber,
            HolderName = account.HolderName,
            Balance = account.Balance
        });
    }

    /// <summary>
    /// 계좌 생성 (테스트용)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AccountResponse>> Create(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            AccountNumber = request.AccountNumber,
            HolderName = request.HolderName
        };

        // 초기 잔액 설정
        if (request.InitialBalance > 0)
        {
            account.Deposit(request.InitialBalance);
        }

        await _unitOfWork.Accounts.AddAsync(account, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = account.Id }, new AccountResponse
        {
            Id = account.Id,
            AccountNumber = account.AccountNumber,
            HolderName = account.HolderName,
            Balance = account.Balance
        });
    }

    /// <summary>
    /// 입금 - 동일 계좌에 대한 병렬 요청도 순차 처리됨
    /// </summary>
    [HttpPost("{id:guid}/deposit")]
    public async Task<ActionResult<OperationResponse>> Deposit(
        Guid id,
        DepositRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Deposit request received for AccountId={AccountId}, Amount={Amount}",
            id, request.Amount);

        var result = await _accountService.DepositAsync(id, request.Amount, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new OperationResponse
            {
                Success = false,
                Message = result.ErrorMessage
            });
        }

        return Ok(new OperationResponse
        {
            Success = true,
            Message = "입금이 완료되었습니다.",
            Data = new { NewBalance = result.NewBalance }
        });
    }

    /// <summary>
    /// 출금 - 동일 계좌에 대한 병렬 요청도 순차 처리됨
    /// </summary>
    [HttpPost("{id:guid}/withdraw")]
    public async Task<ActionResult<OperationResponse>> Withdraw(
        Guid id,
        WithdrawRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: Withdraw request received for AccountId={AccountId}, Amount={Amount}",
            id, request.Amount);

        var result = await _accountService.WithdrawAsync(id, request.Amount, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new OperationResponse
            {
                Success = false,
                Message = result.ErrorMessage
            });
        }

        return Ok(new OperationResponse
        {
            Success = true,
            Message = "출금이 완료되었습니다.",
            Data = new { NewBalance = result.NewBalance }
        });
    }

    /// <summary>
    /// 이체 - 두 계좌 모두에 대해 순차 처리 보장
    /// </summary>
    [HttpPost("transfer")]
    public async Task<ActionResult<OperationResponse>> Transfer(
        TransferRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "API: Transfer request received: SourceId={SourceId}, TargetId={TargetId}, Amount={Amount}",
            request.SourceAccountId, request.TargetAccountId, request.Amount);

        var result = await _accountService.TransferAsync(
            request.SourceAccountId,
            request.TargetAccountId,
            request.Amount,
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new OperationResponse
            {
                Success = false,
                Message = result.ErrorMessage
            });
        }

        return Ok(new OperationResponse
        {
            Success = true,
            Message = "이체가 완료되었습니다.",
            Data = new
            {
                SourceNewBalance = result.SourceNewBalance,
                TargetNewBalance = result.TargetNewBalance
            }
        });
    }
}
