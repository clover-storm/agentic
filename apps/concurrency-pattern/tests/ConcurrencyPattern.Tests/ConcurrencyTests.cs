using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Entities;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Infrastructure.Data;
using ConcurrencyPattern.Infrastructure.Repositories;
using ConcurrencyPattern.Mediator.Services;
using ConcurrencyPattern.SequentialProcessor.Handlers;
using ConcurrencyPattern.SequentialProcessor.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ConcurrencyPattern.Tests;

/// <summary>
/// 동시성 처리 테스트
/// 병렬 요청이 순차 처리되어 데이터 무결성이 보장되는지 검증
/// </summary>
public class ConcurrencyTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AppDbContext _dbContext;

    public ConcurrencyTests()
    {
        var services = new ServiceCollection();

        // InMemory DbContext
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));

        // Logging
        services.AddLogging(builder => builder.AddDebug());

        // Services
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<ISequentialCommandQueue, SequentialCommandQueue>();
        services.AddScoped<ICommandMediator, CommandMediator>();
        services.AddScoped<IAccountService, AccountService>();

        // Handlers
        services.AddScoped<ICommandHandler<DepositCommand, AccountCommandResult>, DepositCommandHandler>();
        services.AddScoped<ICommandHandler<WithdrawCommand, AccountCommandResult>, WithdrawCommandHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<AppDbContext>();
    }

    /// <summary>
    /// 동일 계좌에 대한 병렬 입금이 순차 처리되어 최종 잔액이 정확한지 테스트
    /// </summary>
    [Fact]
    public async Task ParallelDeposits_ShouldBeProcessedSequentially_AndMaintainCorrectBalance()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = new Account
        {
            Id = accountId,
            AccountNumber = "TEST-001",
            HolderName = "Test User"
        };
        account.Deposit(1000m); // 초기 잔액

        await _dbContext.Accounts.AddAsync(account);
        await _dbContext.SaveChangesAsync();

        var accountService = _serviceProvider.GetRequiredService<IAccountService>();
        var depositAmount = 100m;
        var numberOfDeposits = 10;

        // Act - 병렬로 10개의 입금 요청
        var tasks = Enumerable.Range(0, numberOfDeposits)
            .Select(_ => accountService.DepositAsync(accountId, depositAmount))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());

        // 최종 잔액 확인 (초기 1000 + 100 * 10 = 2000)
        var finalAccount = await _dbContext.Accounts.FindAsync(accountId);
        finalAccount!.Balance.Should().Be(1000m + (depositAmount * numberOfDeposits));
    }

    /// <summary>
    /// 동일 계좌에 대한 병렬 출금이 순차 처리되어 잔액 부족 시 실패하는지 테스트
    /// </summary>
    [Fact]
    public async Task ParallelWithdrawals_ShouldBeProcessedSequentially_AndRejectWhenInsufficientBalance()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = new Account
        {
            Id = accountId,
            AccountNumber = "TEST-002",
            HolderName = "Test User"
        };
        account.Deposit(500m); // 초기 잔액 500

        await _dbContext.Accounts.AddAsync(account);
        await _dbContext.SaveChangesAsync();

        var accountService = _serviceProvider.GetRequiredService<IAccountService>();
        var withdrawAmount = 100m;
        var numberOfWithdrawals = 10; // 총 1000 출금 시도 (잔액 500)

        // Act - 병렬로 10개의 출금 요청 (5개만 성공해야 함)
        var tasks = Enumerable.Range(0, numberOfWithdrawals)
            .Select(_ => accountService.WithdrawAsync(accountId, withdrawAmount))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        var successCount = results.Count(r => r.Success);
        var failCount = results.Count(r => !r.Success);

        successCount.Should().Be(5); // 500 / 100 = 5개만 성공
        failCount.Should().Be(5);    // 나머지 5개는 잔액 부족으로 실패

        // 최종 잔액은 0이어야 함
        var finalAccount = await _dbContext.Accounts.FindAsync(accountId);
        finalAccount!.Balance.Should().Be(0m);
    }

    /// <summary>
    /// 서로 다른 계좌에 대한 요청은 병렬로 처리되는지 테스트
    /// </summary>
    [Fact]
    public async Task DifferentAccounts_ShouldBeProcessedInParallel()
    {
        // Arrange
        var accounts = new List<Account>();
        for (int i = 0; i < 5; i++)
        {
            var account = new Account
            {
                Id = Guid.NewGuid(),
                AccountNumber = $"TEST-{i:D3}",
                HolderName = $"User {i}"
            };
            account.Deposit(1000m);
            accounts.Add(account);
        }

        await _dbContext.Accounts.AddRangeAsync(accounts);
        await _dbContext.SaveChangesAsync();

        var accountService = _serviceProvider.GetRequiredService<IAccountService>();

        // Act - 각 계좌에 대해 병렬로 입금
        var tasks = accounts
            .Select(a => accountService.DepositAsync(a.Id, 500m))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());

        foreach (var account in accounts)
        {
            var updated = await _dbContext.Accounts.FindAsync(account.Id);
            updated!.Balance.Should().Be(1500m);
        }
    }

    /// <summary>
    /// 입금과 출금이 혼합된 병렬 요청 테스트
    /// </summary>
    [Fact]
    public async Task MixedOperations_ShouldMaintainDataIntegrity()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = new Account
        {
            Id = accountId,
            AccountNumber = "TEST-MIX",
            HolderName = "Mixed User"
        };
        account.Deposit(1000m);

        await _dbContext.Accounts.AddAsync(account);
        await _dbContext.SaveChangesAsync();

        var accountService = _serviceProvider.GetRequiredService<IAccountService>();

        // Act - 입금과 출금을 번갈아가며 병렬 실행
        var tasks = new List<Task<AccountCommandResult>>();
        for (int i = 0; i < 20; i++)
        {
            if (i % 2 == 0)
                tasks.Add(accountService.DepositAsync(accountId, 100m));
            else
                tasks.Add(accountService.WithdrawAsync(accountId, 50m));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        // 입금: 10 * 100 = 1000
        // 출금: 10 * 50 = 500
        // 최종: 1000 + 1000 - 500 = 1500
        var finalAccount = await _dbContext.Accounts.FindAsync(accountId);

        // 출금이 잔액 부족으로 실패할 수 있으므로 성공한 출금만 계산
        var successfulDeposits = results.Where((r, i) => i % 2 == 0 && r.Success).Count();
        var successfulWithdrawals = results.Where((r, i) => i % 2 == 1 && r.Success).Count();

        var expectedBalance = 1000m + (successfulDeposits * 100m) - (successfulWithdrawals * 50m);
        finalAccount!.Balance.Should().Be(expectedBalance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }
}
