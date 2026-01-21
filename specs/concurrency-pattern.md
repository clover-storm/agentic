# ASP.NET EF Core 동시성 레코드 처리 패턴

## 1. 개요

### 1.1 문제 정의
ASP.NET EF Core 프로젝트에서 동일 레코드에 대한 병렬 요청 시 발생하는 동시성 문제:
- **Lost Update**: 동시에 읽은 데이터를 각각 수정하면 마지막 수정만 반영됨
- **Race Condition**: 잔액 확인 후 출금 사이에 다른 요청이 잔액을 변경
- **데이터 무결성 훼손**: 재고가 음수가 되거나 잔액이 맞지 않는 상황

### 1.2 해결 전략
- **엔티티별 순차 처리**: 동일 엔티티에 대한 요청만 순차 처리, 다른 엔티티는 병렬 처리
- **Channel 기반 큐**: System.Threading.Channels를 사용한 Producer-Consumer 패턴
- **Mediator 패턴**: 요청 라우팅 및 순차 처리 큐 통합
- **Optimistic Concurrency**: EF Core RowVersion으로 2차 방어

---

## 2. 아키텍처

### 2.1 프로젝트 구조
```
ConcurrencyPattern/
├── src/
│   ├── ConcurrencyPattern.Core/           # 엔티티, 인터페이스, 커맨드
│   ├── ConcurrencyPattern.Infrastructure/ # EF Core DbContext, Repository
│   ├── ConcurrencyPattern.SequentialProcessor/ # Channel 기반 순차 처리
│   ├── ConcurrencyPattern.Mediator/       # 요청 조율 및 서비스
│   └── ConcurrencyPattern.Api/            # ASP.NET Core Web API
└── tests/
    └── ConcurrencyPattern.Tests/          # 동시성 테스트
```

### 2.2 핵심 컴포넌트

#### SequentialCommandQueue
```
┌─────────────────────────────────────────────────────────────────┐
│                    SequentialCommandQueue                        │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              ConcurrentDictionary<EntityKey, Queue>      │   │
│  │  ┌───────────────┐  ┌───────────────┐  ┌─────────────┐  │   │
│  │  │ Account:ID-1  │  │ Account:ID-2  │  │ Inventory:X │  │   │
│  │  │  ┌─────────┐  │  │  ┌─────────┐  │  │ ┌─────────┐ │  │   │
│  │  │  │ Channel │  │  │  │ Channel │  │  │ │ Channel │ │  │   │
│  │  │  │ ────────│  │  │  │ ────────│  │  │ │ ────────│ │  │   │
│  │  │  │ CMD1    │  │  │  │ CMD3    │  │  │ │ CMD5    │ │  │   │
│  │  │  │ CMD2    │  │  │  │         │  │  │ │ CMD6    │ │  │   │
│  │  │  └─────────┘  │  │  └─────────┘  │  │ └─────────┘ │  │   │
│  │  │  ↓ Consumer   │  │  ↓ Consumer   │  │ ↓ Consumer  │  │   │
│  │  └───────────────┘  └───────────────┘  └─────────────┘  │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  * 같은 엔티티 요청 → 같은 큐 → 순차 처리                        │
│  * 다른 엔티티 요청 → 다른 큐 → 병렬 처리                        │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. 핵심 구현

### 3.1 엔티티 커맨드 인터페이스
```csharp
public interface IEntityCommand<TResult> : ICommand<TResult>
{
    Guid EntityId { get; }      // 대상 엔티티 ID
    string EntityType { get; }  // 엔티티 타입 (Account, Inventory 등)
}
```

### 3.2 Channel 기반 순차 처리
```csharp
// 엔티티별 독립 큐
_channel = Channel.CreateUnbounded<CommandEnvelope>(new UnboundedChannelOptions
{
    SingleReader = true,  // 단일 Consumer로 순차 처리 보장
    SingleWriter = false  // 여러 Producer 허용 (병렬 요청 수용)
});

// 백그라운드 Consumer
await foreach (var envelope in _channel.Reader.ReadAllAsync(ct))
{
    await envelope.ExecuteAsync(ct);  // 하나씩 순차 실행
}
```

### 3.3 복합 커맨드 (다중 엔티티 잠금)
```csharp
// 이체: 두 계좌 모두 잠금 필요
var entities = new[]
{
    ("Account", sourceAccountId),
    ("Account", targetAccountId)
};

// 데드락 방지: 항상 같은 순서로 잠금
var sortedEntities = entities.OrderBy(k => k).ToList();
foreach (var entityKey in sortedEntities)
{
    await _entityLocks[entityKey].WaitAsync(ct);
}
```

### 3.4 EF Core Optimistic Concurrency
```csharp
// DbContext 설정
entity.Property(e => e.RowVersion)
    .IsRowVersion()
    .IsConcurrencyToken();

// 충돌 시 예외 변환
catch (DbUpdateConcurrencyException ex)
{
    throw new ConcurrencyException(entityType, entityId, ex);
}
```

---

## 4. 사용 예시

### 4.1 입금 처리
```csharp
// AccountService
public async Task<AccountCommandResult> DepositAsync(Guid accountId, decimal amount)
{
    var command = new DepositCommand
    {
        AccountId = accountId,  // EntityId로 사용
        Amount = amount
    };

    // Mediator가 순차 처리 큐로 전달
    return await _mediator.SendAsync(command);
}
```

### 4.2 병렬 요청 흐름
```
Client A: POST /accounts/{id}/deposit  ─┐
Client B: POST /accounts/{id}/deposit  ─┤→ SequentialQueue[Account:{id}]
Client C: POST /accounts/{id}/deposit  ─┘      │
                                               ↓
                                         순차 처리
                                         1. A 입금
                                         2. B 입금
                                         3. C 입금
```

---

## 5. 테스트 시나리오

### 5.1 병렬 입금 테스트
```csharp
[Fact]
public async Task ParallelDeposits_ShouldMaintainCorrectBalance()
{
    // 10개의 병렬 입금 요청
    var tasks = Enumerable.Range(0, 10)
        .Select(_ => accountService.DepositAsync(accountId, 100m));

    await Task.WhenAll(tasks);

    // 최종 잔액: 초기 1000 + (100 * 10) = 2000
    account.Balance.Should().Be(2000m);
}
```

### 5.2 병렬 출금 테스트 (잔액 부족)
```csharp
[Fact]
public async Task ParallelWithdrawals_ShouldRejectWhenInsufficient()
{
    // 잔액 500, 100원씩 10번 출금 시도
    var tasks = Enumerable.Range(0, 10)
        .Select(_ => accountService.WithdrawAsync(accountId, 100m));

    var results = await Task.WhenAll(tasks);

    // 5개 성공, 5개 실패, 최종 잔액 0
    results.Count(r => r.Success).Should().Be(5);
    account.Balance.Should().Be(0m);
}
```

---

## 6. 장단점

### 6.1 장점
- **높은 처리량**: 다른 엔티티 요청은 병렬 처리
- **강력한 무결성**: 동일 엔티티 요청은 확실히 순차 처리
- **확장성**: 분산 환경에서는 Redis 등으로 확장 가능
- **투명성**: 비즈니스 로직 변경 없이 적용 가능

### 6.2 단점
- **메모리 사용**: 엔티티별 큐 유지 (유휴 큐 정리로 완화)
- **복잡성**: 단순 낙관적 잠금보다 구현 복잡
- **지연 가능성**: 특정 엔티티에 요청 집중 시 큐잉 지연

### 6.3 대안 비교
| 방식 | 처리량 | 무결성 | 복잡도 |
|------|--------|--------|--------|
| DB 비관적 잠금 | 낮음 | 높음 | 낮음 |
| DB 낙관적 잠금 | 높음 | 중간 | 낮음 |
| **Channel 순차 처리** | **높음** | **높음** | **중간** |
| 분산 락 (Redis) | 높음 | 높음 | 높음 |

---

## 7. 확장 방향

### 7.1 분산 환경
```csharp
// Redis 기반 분산 락으로 확장
public class DistributedSequentialQueue : ISequentialCommandQueue
{
    private readonly IDistributedLockFactory _lockFactory;

    public async Task<TResult> EnqueueAsync<TResult>(IEntityCommand<TResult> command)
    {
        var lockKey = $"lock:{command.EntityType}:{command.EntityId}";
        await using var @lock = await _lockFactory.CreateLockAsync(lockKey);
        return await ExecuteCommandAsync(command);
    }
}
```

### 7.2 재시도 정책
```csharp
// Polly를 사용한 재시도
var retryPolicy = Policy
    .Handle<ConcurrencyException>()
    .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(100 * i));

await retryPolicy.ExecuteAsync(() => _mediator.SendAsync(command));
```

---

## 8. API 엔드포인트

### 8.1 계좌 API
| Method | Endpoint | 설명 |
|--------|----------|------|
| GET | /api/accounts | 계좌 목록 |
| GET | /api/accounts/{id} | 계좌 상세 |
| POST | /api/accounts | 계좌 생성 |
| POST | /api/accounts/{id}/deposit | 입금 |
| POST | /api/accounts/{id}/withdraw | 출금 |
| POST | /api/accounts/transfer | 이체 |

### 8.2 재고 API
| Method | Endpoint | 설명 |
|--------|----------|------|
| GET | /api/inventories | 재고 목록 |
| GET | /api/inventories/{id} | 재고 상세 |
| POST | /api/inventories | 재고 생성 |
| POST | /api/inventories/{id}/add-stock | 재고 추가 |
| POST | /api/inventories/{id}/reserve | 재고 예약 |
| POST | /api/inventories/{id}/confirm | 예약 확정 |
| POST | /api/inventories/{id}/cancel | 예약 취소 |

---

## 9. 참고 자료

- [System.Threading.Channels](https://docs.microsoft.com/en-us/dotnet/core/extensions/channels)
- [EF Core Concurrency Tokens](https://docs.microsoft.com/en-us/ef/core/saving/concurrency)
- [Mediator Pattern](https://refactoring.guru/design-patterns/mediator)
