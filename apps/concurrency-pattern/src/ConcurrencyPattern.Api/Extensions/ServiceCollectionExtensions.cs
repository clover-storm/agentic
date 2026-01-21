using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Infrastructure.Data;
using ConcurrencyPattern.Infrastructure.Repositories;
using ConcurrencyPattern.Mediator.Services;
using ConcurrencyPattern.SequentialProcessor.Handlers;
using ConcurrencyPattern.SequentialProcessor.Services;
using Microsoft.EntityFrameworkCore;

namespace ConcurrencyPattern.Api.Extensions;

/// <summary>
/// 서비스 등록 확장 메서드
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 애플리케이션 서비스 등록
    /// </summary>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // DbContext 등록 (InMemory 또는 SQL Server)
        var useInMemory = configuration.GetValue<bool>("UseInMemoryDatabase", true);

        if (useInMemory)
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("ConcurrencyPatternDb"));
        }
        else
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
        }

        // Repository 및 UnitOfWork 등록
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // 순차 처리 큐 등록 (Singleton - 전역 상태 관리)
        services.AddSingleton<ISequentialCommandQueue, SequentialCommandQueue>();

        // Mediator 등록
        services.AddScoped<ICommandMediator, CommandMediator>();

        // 비즈니스 서비스 등록
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IInventoryService, InventoryService>();

        // 커맨드 핸들러 등록
        services.AddCommandHandlers();

        return services;
    }

    /// <summary>
    /// 커맨드 핸들러 등록
    /// </summary>
    private static IServiceCollection AddCommandHandlers(this IServiceCollection services)
    {
        // Account 핸들러
        services.AddScoped<ICommandHandler<DepositCommand, AccountCommandResult>, DepositCommandHandler>();
        services.AddScoped<ICommandHandler<WithdrawCommand, AccountCommandResult>, WithdrawCommandHandler>();
        services.AddScoped<ICommandHandler<TransferCommand, TransferCommandResult>, TransferCommandHandler>();

        // Inventory 핸들러
        services.AddScoped<ICommandHandler<AddStockCommand, InventoryCommandResult>, AddStockCommandHandler>();
        services.AddScoped<ICommandHandler<ReserveStockCommand, InventoryCommandResult>, ReserveStockCommandHandler>();
        services.AddScoped<ICommandHandler<ConfirmReservationCommand, InventoryCommandResult>, ConfirmReservationCommandHandler>();
        services.AddScoped<ICommandHandler<CancelReservationCommand, InventoryCommandResult>, CancelReservationCommandHandler>();

        return services;
    }
}
