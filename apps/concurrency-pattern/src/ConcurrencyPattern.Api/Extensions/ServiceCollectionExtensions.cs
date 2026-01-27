using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Infrastructure.Data;
using ConcurrencyPattern.Infrastructure.Repositories;
using ConcurrencyPattern.Infrastructure.Redis;
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
        var useInMemoryDb = configuration.GetValue<bool>("UseInMemoryDatabase", true);

        if (useInMemoryDb)
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

        // 순차 처리 큐 등록 (Redis 또는 InMemory)
        var useRedis = configuration.GetValue<bool>("UseRedisQueue", false);

        if (useRedis)
        {
            var useStrandMode = configuration.GetValue<bool>("Redis:UseStrandMode", false);
            var useEventDriven = configuration.GetValue<bool>("Redis:UseEventDrivenStrand", false);

            if (useStrandMode && useEventDriven)
            {
                // Strand + Pub/Sub 하이브리드 (N개 프로세스, 빠른 반응)
                services.AddRedisEventDrivenStrandInfrastructure(configuration);
            }
            else if (useStrandMode)
            {
                // Strand Polling 방식 (N개 프로세스, 안정적)
                services.AddRedisStrandInfrastructure(configuration);
            }
            else
            {
                // 기존 Context Consumer 방식 (단일 프로세스)
                services.AddRedisInfrastructure(configuration);
            }
        }
        else
        {
            // 인메모리 Channel 기반 큐 (단일 인스턴스)
            services.AddSingleton<ISequentialCommandQueue, SequentialCommandQueue>();
        }

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
