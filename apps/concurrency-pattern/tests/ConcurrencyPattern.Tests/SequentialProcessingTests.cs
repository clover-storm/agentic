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
using Xunit;

namespace ConcurrencyPattern.Tests;

/// <summary>
/// 순차 처리 보장 테스트
/// Channel 기반 인메모리 구현으로 순차 처리가 보장되는지 검증
/// </summary>
public class SequentialProcessingTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AppDbContext _dbContext;
    private readonly ISequentialCommandQueue _commandQueue;

    public SequentialProcessingTests()
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));

        services.AddLogging(builder => builder.AddDebug());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<ISequentialCommandQueue, SequentialCommandQueue>();
        services.AddScoped<ICommandMediator, CommandMediator>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IInventoryService, InventoryService>();

        services.AddScoped<ICommandHandler<DepositCommand, AccountCommandResult>, DepositCommandHandler>();
        services.AddScoped<ICommandHandler<WithdrawCommand, AccountCommandResult>, WithdrawCommandHandler>();
        services.AddScoped<ICommandHandler<AdjustStockCommand, InventoryCommandResult>, AdjustStockCommandHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<AppDbContext>();
        _commandQueue = _serviceProvider.GetRequiredService<ISequentialCommandQueue>();
    }

    /// <summary>
    /// 동일 엔티티에 대한 동시 요청이 순차적으로 처리되는지 검증
    /// 잔액이 정확해야 순차 처리가 보장된 것
    /// </summary>
    [Fact]
    public async Task SameEntity_ConcurrentRequests_ShouldBeProcessedSequentially()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = new Account
        {
            Id = accountId,
            AccountNumber = "SEQ-001",
            HolderName = "Sequential Test"
        };
        account.Deposit(1000m);

        await _dbContext.Accounts.AddAsync(account);
        await _dbContext.SaveChangesAsync();

        var accountService = _serviceProvider.GetRequiredService<IAccountService>();
        var depositAmount = 10m;
        var numberOfDeposits = 100;

        // Act - 100개 동시 요청
        var tasks = Enumerable.Range(0, numberOfDeposits)
            .Select(_ => accountService.DepositAsync(accountId, depositAmount))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(numberOfDeposits);
        results.All(r => r.Success).Should().BeTrue();

        // 새로운 컨텍스트로 검증 (캐시 무효화)
        using var verifyScope = _serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var finalAccount = await verifyContext.Accounts.FindAsync(accountId);

        // 정확한 잔액 = 초기 1000 + (10 * 100) = 2000
        finalAccount!.Balance.Should().Be(1000m + (depositAmount * numberOfDeposits));
    }

    /// <summary>
    /// 서로 다른 엔티티에 대한 요청은 병렬로 처리되는지 검증
    /// 각 엔티티의 최종 상태가 정확해야 함
    /// </summary>
    [Fact]
    public async Task DifferentEntities_ShouldBeProcessedInParallel()
    {
        // Arrange
        var accounts = new List<Account>();
        for (int i = 0; i < 10; i++)
        {
            var account = new Account
            {
                Id = Guid.NewGuid(),
                AccountNumber = $"PAR-{i:D3}",
                HolderName = $"Parallel User {i}"
            };
            account.Deposit(100m);
            accounts.Add(account);
        }

        await _dbContext.Accounts.AddRangeAsync(accounts);
        await _dbContext.SaveChangesAsync();

        var accountService = _serviceProvider.GetRequiredService<IAccountService>();

        // Act - 각 계좌에 동시에 10번씩 입금
        var allTasks = new List<Task<AccountCommandResult>>();
        foreach (var account in accounts)
        {
            for (int i = 0; i < 10; i++)
            {
                allTasks.Add(accountService.DepositAsync(account.Id, 50m));
            }
        }

        var results = await Task.WhenAll(allTasks);

        // Assert
        results.Should().HaveCount(100);
        results.All(r => r.Success).Should().BeTrue();

        // 각 계좌 잔액 확인 = 초기 100 + (50 * 10) = 600
        using var verifyScope = _serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var account in accounts)
        {
            var finalAccount = await verifyContext.Accounts.FindAsync(account.Id);
            finalAccount!.Balance.Should().Be(100m + (50m * 10));
        }
    }

    /// <summary>
    /// 서로 다른 컨텍스트(Account vs Inventory)는 완전히 독립적으로 처리되는지 검증
    /// </summary>
    [Fact]
    public async Task DifferentContexts_ShouldBeFullyIndependent()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();

        var account = new Account
        {
            Id = accountId,
            AccountNumber = "CTX-001",
            HolderName = "Context Test"
        };
        account.Deposit(1000m);

        var inventory = new Inventory
        {
            Id = inventoryId,
            ProductCode = "CTX-PROD-001",
            ProductName = "Context Test Product",
            Quantity = 100
        };

        await _dbContext.Accounts.AddAsync(account);
        await _dbContext.Inventories.AddAsync(inventory);
        await _dbContext.SaveChangesAsync();

        var accountService = _serviceProvider.GetRequiredService<IAccountService>();
        var inventoryService = _serviceProvider.GetRequiredService<IInventoryService>();

        // Act - 두 컨텍스트에 동시 요청
        var accountTasks = Enumerable.Range(0, 20)
            .Select(_ => accountService.DepositAsync(accountId, 10m))
            .ToList();

        var inventoryTasks = Enumerable.Range(0, 20)
            .Select(_ => inventoryService.AdjustStockAsync(inventoryId, 5))
            .ToList();

        var allTasks = accountTasks.Cast<Task>().Concat(inventoryTasks.Cast<Task>()).ToList();
        await Task.WhenAll(allTasks);

        // Assert
        using var verifyScope = _serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var finalAccount = await verifyContext.Accounts.FindAsync(accountId);
        var finalInventory = await verifyContext.Inventories.FindAsync(inventoryId);

        // Account: 1000 + (10 * 20) = 1200
        finalAccount!.Balance.Should().Be(1000m + (10m * 20));

        // Inventory: 100 + (5 * 20) = 200
        finalInventory!.Quantity.Should().Be(100 + (5 * 20));
    }

    /// <summary>
    /// 잔액 부족 시 출금이 순차적으로 거부되는지 검증
    /// </summary>
    [Fact]
    public async Task InsufficientBalance_ShouldRejectWithdrawalsSequentially()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = new Account
        {
            Id = accountId,
            AccountNumber = "REJ-001",
            HolderName = "Reject Test"
        };
        account.Deposit(300m); // 초기 잔액 300

        await _dbContext.Accounts.AddAsync(account);
        await _dbContext.SaveChangesAsync();

        var accountService = _serviceProvider.GetRequiredService<IAccountService>();

        // Act - 100씩 5번 출금 시도 (최대 3번만 성공 가능)
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => accountService.WithdrawAsync(accountId, 100m))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        var successCount = results.Count(r => r.Success);
        var failCount = results.Count(r => !r.Success);

        successCount.Should().Be(3); // 300 / 100 = 3
        failCount.Should().Be(2);

        using var verifyScope = _serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var finalAccount = await verifyContext.Accounts.FindAsync(accountId);

        finalAccount!.Balance.Should().Be(0m);
    }

    /// <summary>
    /// 입출금 혼합 시나리오에서 데이터 무결성 유지 검증
    /// </summary>
    [Fact]
    public async Task MixedDepositWithdraw_ShouldMaintainIntegrity()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = new Account
        {
            Id = accountId,
            AccountNumber = "MIX-001",
            HolderName = "Mixed Test"
        };
        account.Deposit(5000m);

        await _dbContext.Accounts.AddAsync(account);
        await _dbContext.SaveChangesAsync();

        var accountService = _serviceProvider.GetRequiredService<IAccountService>();

        // Act - 입금과 출금 혼합
        var tasks = new List<Task<AccountCommandResult>>();

        // 50번 입금 (각 100원)
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(accountService.DepositAsync(accountId, 100m));
        }

        // 50번 출금 (각 50원)
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(accountService.WithdrawAsync(accountId, 50m));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        var allDepositsSucceeded = results.Take(50).All(r => r.Success);
        allDepositsSucceeded.Should().BeTrue();

        // 출금은 모두 성공해야 함 (충분한 잔액)
        var allWithdrawsSucceeded = results.Skip(50).All(r => r.Success);
        allWithdrawsSucceeded.Should().BeTrue();

        using var verifyScope = _serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var finalAccount = await verifyContext.Accounts.FindAsync(accountId);

        // 5000 + (100 * 50) - (50 * 50) = 5000 + 5000 - 2500 = 7500
        finalAccount!.Balance.Should().Be(5000m + (100m * 50) - (50m * 50));
    }

    /// <summary>
    /// 재고 부족 시 출고가 순차적으로 거부되는지 검증
    /// </summary>
    [Fact]
    public async Task InsufficientStock_ShouldRejectAdjustmentsSequentially()
    {
        // Arrange
        var inventoryId = Guid.NewGuid();
        var inventory = new Inventory
        {
            Id = inventoryId,
            ProductCode = "STK-001",
            ProductName = "Stock Test Product",
            Quantity = 25
        };

        await _dbContext.Inventories.AddAsync(inventory);
        await _dbContext.SaveChangesAsync();

        var inventoryService = _serviceProvider.GetRequiredService<IInventoryService>();

        // Act - -10씩 5번 출고 시도 (최대 2번만 성공 가능)
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => inventoryService.AdjustStockAsync(inventoryId, -10))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        var successCount = results.Count(r => r.Success);
        var failCount = results.Count(r => !r.Success);

        successCount.Should().Be(2); // 25 / 10 = 2 (나머지 5는 출고 불가)
        failCount.Should().Be(3);

        using var verifyScope = _serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var finalInventory = await verifyContext.Inventories.FindAsync(inventoryId);

        finalInventory!.Quantity.Should().Be(5); // 25 - 20 = 5
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }
}
