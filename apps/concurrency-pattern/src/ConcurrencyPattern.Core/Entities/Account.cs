namespace ConcurrencyPattern.Core.Entities;

/// <summary>
/// 계좌 엔티티 - 동시성 처리가 필요한 대표적인 예시
/// 잔액 변경 시 동시 접근으로 인한 Lost Update 방지 필요
/// </summary>
public class Account : BaseEntity
{
    public string AccountNumber { get; set; } = string.Empty;
    public string HolderName { get; set; } = string.Empty;
    public decimal Balance { get; private set; }

    public void Deposit(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("입금액은 0보다 커야 합니다.", nameof(amount));

        Balance += amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Withdraw(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("출금액은 0보다 커야 합니다.", nameof(amount));

        if (Balance < amount)
            throw new InvalidOperationException($"잔액 부족. 현재 잔액: {Balance}, 요청 금액: {amount}");

        Balance -= amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Transfer(Account target, decimal amount)
    {
        Withdraw(amount);
        target.Deposit(amount);
    }
}
