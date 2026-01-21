namespace ConcurrencyPattern.Api.DTOs;

/// <summary>
/// 재고 추가 요청 DTO
/// </summary>
public record AddStockRequest
{
    public int Quantity { get; init; }
}

/// <summary>
/// 재고 예약 요청 DTO
/// </summary>
public record ReserveStockRequest
{
    public int Quantity { get; init; }
    public Guid OrderId { get; init; }
}

/// <summary>
/// 재고 생성 요청 DTO
/// </summary>
public record CreateInventoryRequest
{
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int InitialQuantity { get; init; }
}

/// <summary>
/// 재고 응답 DTO
/// </summary>
public record InventoryResponse
{
    public Guid Id { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public int ReservedQuantity { get; init; }
    public int AvailableQuantity { get; init; }
}
