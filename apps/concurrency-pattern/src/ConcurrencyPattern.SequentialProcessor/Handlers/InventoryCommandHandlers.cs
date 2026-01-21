using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Exceptions;
using ConcurrencyPattern.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ConcurrencyPattern.SequentialProcessor.Handlers;

/// <summary>
/// 재고 추가 커맨드 핸들러
/// </summary>
public class AddStockCommandHandler : ICommandHandler<AddStockCommand, InventoryCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AddStockCommandHandler> _logger;

    public AddStockCommandHandler(IUnitOfWork unitOfWork, ILogger<AddStockCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<InventoryCommandResult> HandleAsync(
        AddStockCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing add stock: InventoryId={InventoryId}, Quantity={Quantity}",
            command.InventoryId, command.Quantity);

        try
        {
            var inventory = await _unitOfWork.Inventories.GetByIdAsync(command.InventoryId, cancellationToken);

            if (inventory == null)
            {
                return new InventoryCommandResult
                {
                    Success = false,
                    ErrorMessage = $"재고 항목을 찾을 수 없습니다: {command.InventoryId}"
                };
            }

            inventory.AddStock(command.Quantity);
            await _unitOfWork.Inventories.UpdateAsync(inventory, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Stock added: InventoryId={InventoryId}, CurrentQuantity={Quantity}",
                command.InventoryId, inventory.Quantity);

            return new InventoryCommandResult
            {
                Success = true,
                CurrentQuantity = inventory.Quantity,
                AvailableQuantity = inventory.AvailableQuantity
            };
        }
        catch (ConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during add stock for InventoryId={InventoryId}",
                command.InventoryId);

            return new InventoryCommandResult
            {
                Success = false,
                ErrorMessage = "동시성 충돌이 발생했습니다. 다시 시도해주세요."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during add stock for InventoryId={InventoryId}", command.InventoryId);

            return new InventoryCommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// 재고 예약 커맨드 핸들러
/// </summary>
public class ReserveStockCommandHandler : ICommandHandler<ReserveStockCommand, InventoryCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReserveStockCommandHandler> _logger;

    public ReserveStockCommandHandler(IUnitOfWork unitOfWork, ILogger<ReserveStockCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<InventoryCommandResult> HandleAsync(
        ReserveStockCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Processing reserve stock: InventoryId={InventoryId}, Quantity={Quantity}, OrderId={OrderId}",
            command.InventoryId, command.Quantity, command.OrderId);

        try
        {
            var inventory = await _unitOfWork.Inventories.GetByIdAsync(command.InventoryId, cancellationToken);

            if (inventory == null)
            {
                return new InventoryCommandResult
                {
                    Success = false,
                    ErrorMessage = $"재고 항목을 찾을 수 없습니다: {command.InventoryId}"
                };
            }

            inventory.Reserve(command.Quantity);
            await _unitOfWork.Inventories.UpdateAsync(inventory, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Stock reserved: InventoryId={InventoryId}, AvailableQuantity={Available}",
                command.InventoryId, inventory.AvailableQuantity);

            return new InventoryCommandResult
            {
                Success = true,
                CurrentQuantity = inventory.Quantity,
                AvailableQuantity = inventory.AvailableQuantity
            };
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("가용 재고 부족"))
        {
            _logger.LogWarning("Insufficient stock for InventoryId={InventoryId}", command.InventoryId);

            return new InventoryCommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (ConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during reserve stock for InventoryId={InventoryId}",
                command.InventoryId);

            return new InventoryCommandResult
            {
                Success = false,
                ErrorMessage = "동시성 충돌이 발생했습니다. 다시 시도해주세요."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during reserve stock for InventoryId={InventoryId}", command.InventoryId);

            return new InventoryCommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// 예약 확정 커맨드 핸들러
/// </summary>
public class ConfirmReservationCommandHandler : ICommandHandler<ConfirmReservationCommand, InventoryCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmReservationCommandHandler> _logger;

    public ConfirmReservationCommandHandler(IUnitOfWork unitOfWork, ILogger<ConfirmReservationCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<InventoryCommandResult> HandleAsync(
        ConfirmReservationCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Processing confirm reservation: InventoryId={InventoryId}, Quantity={Quantity}, OrderId={OrderId}",
            command.InventoryId, command.Quantity, command.OrderId);

        try
        {
            var inventory = await _unitOfWork.Inventories.GetByIdAsync(command.InventoryId, cancellationToken);

            if (inventory == null)
            {
                return new InventoryCommandResult
                {
                    Success = false,
                    ErrorMessage = $"재고 항목을 찾을 수 없습니다: {command.InventoryId}"
                };
            }

            inventory.ConfirmReservation(command.Quantity);
            await _unitOfWork.Inventories.UpdateAsync(inventory, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Reservation confirmed: InventoryId={InventoryId}, CurrentQuantity={Quantity}",
                command.InventoryId, inventory.Quantity);

            return new InventoryCommandResult
            {
                Success = true,
                CurrentQuantity = inventory.Quantity,
                AvailableQuantity = inventory.AvailableQuantity
            };
        }
        catch (ConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during confirm reservation for InventoryId={InventoryId}",
                command.InventoryId);

            return new InventoryCommandResult
            {
                Success = false,
                ErrorMessage = "동시성 충돌이 발생했습니다. 다시 시도해주세요."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during confirm reservation for InventoryId={InventoryId}",
                command.InventoryId);

            return new InventoryCommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// 예약 취소 커맨드 핸들러
/// </summary>
public class CancelReservationCommandHandler : ICommandHandler<CancelReservationCommand, InventoryCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CancelReservationCommandHandler> _logger;

    public CancelReservationCommandHandler(IUnitOfWork unitOfWork, ILogger<CancelReservationCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<InventoryCommandResult> HandleAsync(
        CancelReservationCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Processing cancel reservation: InventoryId={InventoryId}, Quantity={Quantity}, OrderId={OrderId}",
            command.InventoryId, command.Quantity, command.OrderId);

        try
        {
            var inventory = await _unitOfWork.Inventories.GetByIdAsync(command.InventoryId, cancellationToken);

            if (inventory == null)
            {
                return new InventoryCommandResult
                {
                    Success = false,
                    ErrorMessage = $"재고 항목을 찾을 수 없습니다: {command.InventoryId}"
                };
            }

            inventory.CancelReservation(command.Quantity);
            await _unitOfWork.Inventories.UpdateAsync(inventory, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Reservation cancelled: InventoryId={InventoryId}, AvailableQuantity={Available}",
                command.InventoryId, inventory.AvailableQuantity);

            return new InventoryCommandResult
            {
                Success = true,
                CurrentQuantity = inventory.Quantity,
                AvailableQuantity = inventory.AvailableQuantity
            };
        }
        catch (ConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during cancel reservation for InventoryId={InventoryId}",
                command.InventoryId);

            return new InventoryCommandResult
            {
                Success = false,
                ErrorMessage = "동시성 충돌이 발생했습니다. 다시 시도해주세요."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cancel reservation for InventoryId={InventoryId}",
                command.InventoryId);

            return new InventoryCommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
