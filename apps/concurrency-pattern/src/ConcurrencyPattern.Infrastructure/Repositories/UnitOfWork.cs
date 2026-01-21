using ConcurrencyPattern.Core.Entities;
using ConcurrencyPattern.Core.Exceptions;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ConcurrencyPattern.Infrastructure.Repositories;

/// <summary>
/// Unit of Work 패턴 구현
/// 트랜잭션 관리 및 동시성 예외 처리
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _transaction;

    private IRepository<Account>? _accounts;
    private IRepository<Inventory>? _inventories;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IRepository<Account> Accounts =>
        _accounts ??= new Repository<Account>(_context);

    public IRepository<Inventory> Inventories =>
        _inventories ??= new Repository<Inventory>(_context);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // 동시성 충돌 시 의미 있는 예외로 변환
            var entry = ex.Entries.FirstOrDefault();
            if (entry != null)
            {
                var entityType = entry.Entity.GetType().Name;
                var entityId = (entry.Entity as BaseEntity)?.Id ?? Guid.Empty;
                throw new ConcurrencyException(entityType, entityId, ex);
            }
            throw;
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
    }
}
