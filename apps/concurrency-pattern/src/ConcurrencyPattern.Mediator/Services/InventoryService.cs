using ConcurrencyPattern.Core.Commands;
using Microsoft.Extensions.Logging;

namespace ConcurrencyPattern.Mediator.Services;

/// <summary>
/// 재고 서비스 - 비즈니스 로직 진입점
/// </summary>
public interface IInventoryService
{
    Task<InventoryCommandResult> AddStockAsync(Guid inventoryId, int quantity, CancellationToken cancellationToken = default);
    Task<InventoryCommandResult> ReserveStockAsync(Guid inventoryId, int quantity, Guid orderId, CancellationToken cancellationToken = default);
    Task<InventoryCommandResult> ConfirmReservationAsync(Guid inventoryId, int quantity, Guid orderId, CancellationToken cancellationToken = default);
    Task<InventoryCommandResult> CancelReservationAsync(Guid inventoryId, int quantity, Guid orderId, CancellationToken cancellationToken = default);
}

public class InventoryService : IInventoryService
{
    private readonly ICommandMediator _mediator;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(ICommandMediator mediator, ILogger<InventoryService> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// 재고 추가
    /// 동일 재고 항목에 대한 동시 요청은 순차 처리됨
    /// </summary>
    public async Task<InventoryCommandResult> AddStockAsync(
        Guid inventoryId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Add stock request: InventoryId={InventoryId}, Quantity={Quantity}",
            inventoryId, quantity);

        var command = new AddStockCommand
        {
            InventoryId = inventoryId,
            Quantity = quantity
        };

        return await _mediator.SendAsync(command, cancellationToken);
    }

    /// <summary>
    /// 재고 예약
    /// 동일 재고 항목에 대한 동시 주문은 순차 처리됨
    /// </summary>
    public async Task<InventoryCommandResult> ReserveStockAsync(
        Guid inventoryId,
        int quantity,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Reserve stock request: InventoryId={InventoryId}, Quantity={Quantity}, OrderId={OrderId}",
            inventoryId, quantity, orderId);

        var command = new ReserveStockCommand
        {
            InventoryId = inventoryId,
            Quantity = quantity,
            OrderId = orderId
        };

        return await _mediator.SendAsync(command, cancellationToken);
    }

    /// <summary>
    /// 예약 확정
    /// </summary>
    public async Task<InventoryCommandResult> ConfirmReservationAsync(
        Guid inventoryId,
        int quantity,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Confirm reservation request: InventoryId={InventoryId}, Quantity={Quantity}, OrderId={OrderId}",
            inventoryId, quantity, orderId);

        var command = new ConfirmReservationCommand
        {
            InventoryId = inventoryId,
            Quantity = quantity,
            OrderId = orderId
        };

        return await _mediator.SendAsync(command, cancellationToken);
    }

    /// <summary>
    /// 예약 취소
    /// </summary>
    public async Task<InventoryCommandResult> CancelReservationAsync(
        Guid inventoryId,
        int quantity,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Cancel reservation request: InventoryId={InventoryId}, Quantity={Quantity}, OrderId={OrderId}",
            inventoryId, quantity, orderId);

        var command = new CancelReservationCommand
        {
            InventoryId = inventoryId,
            Quantity = quantity,
            OrderId = orderId
        };

        return await _mediator.SendAsync(command, cancellationToken);
    }
}
