namespace ConcurrencyPattern.Core.Commands;

/// <summary>
/// 재고 커맨드 기본 클래스
/// </summary>
public abstract record InventoryCommandBase : IEntityCommand<InventoryCommandResult>
{
    public Guid CommandId { get; } = Guid.NewGuid();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public required Guid InventoryId { get; init; }

    public Guid EntityId => InventoryId;
    public string EntityType => "Inventory";
}

/// <summary>
/// 재고 추가 커맨드
/// </summary>
public record AddStockCommand : InventoryCommandBase
{
    public required int Quantity { get; init; }
}

/// <summary>
/// 재고 예약 커맨드
/// </summary>
public record ReserveStockCommand : InventoryCommandBase
{
    public required int Quantity { get; init; }
    public required Guid OrderId { get; init; }
}

/// <summary>
/// 예약 확정 커맨드
/// </summary>
public record ConfirmReservationCommand : InventoryCommandBase
{
    public required int Quantity { get; init; }
    public required Guid OrderId { get; init; }
}

/// <summary>
/// 예약 취소 커맨드
/// </summary>
public record CancelReservationCommand : InventoryCommandBase
{
    public required int Quantity { get; init; }
    public required Guid OrderId { get; init; }
}

/// <summary>
/// 재고 커맨드 결과
/// </summary>
public record InventoryCommandResult
{
    public bool Success { get; init; }
    public int CurrentQuantity { get; init; }
    public int AvailableQuantity { get; init; }
    public string? ErrorMessage { get; init; }
}
