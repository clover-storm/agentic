namespace ConcurrencyPattern.Core.Entities;

/// <summary>
/// 재고 엔티티 - 동시 주문 시 재고 무결성 보장 필요
/// </summary>
public class Inventory : BaseEntity
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; private set; }
    public int ReservedQuantity { get; private set; }

    /// <summary>
    /// 실제 사용 가능한 재고 수량
    /// </summary>
    public int AvailableQuantity => Quantity - ReservedQuantity;

    public void AddStock(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("추가 수량은 0보다 커야 합니다.", nameof(quantity));

        Quantity += quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reserve(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("예약 수량은 0보다 커야 합니다.", nameof(quantity));

        if (AvailableQuantity < quantity)
            throw new InvalidOperationException($"가용 재고 부족. 가용: {AvailableQuantity}, 요청: {quantity}");

        ReservedQuantity += quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ConfirmReservation(int quantity)
    {
        if (quantity <= 0 || quantity > ReservedQuantity)
            throw new ArgumentException("확정 수량이 유효하지 않습니다.", nameof(quantity));

        ReservedQuantity -= quantity;
        Quantity -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void CancelReservation(int quantity)
    {
        if (quantity <= 0 || quantity > ReservedQuantity)
            throw new ArgumentException("취소 수량이 유효하지 않습니다.", nameof(quantity));

        ReservedQuantity -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }
}
