using ConcurrencyPattern.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace ConcurrencyPattern.Infrastructure.Data;

/// <summary>
/// 애플리케이션 DbContext
/// RowVersion을 통한 낙관적 동시성 제어 설정
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Inventory> Inventories => Set<Inventory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Account 엔티티 설정
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.AccountNumber)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.HolderName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Balance)
                .HasPrecision(18, 2);

            // Optimistic Concurrency - RowVersion 설정
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasIndex(e => e.AccountNumber).IsUnique();
        });

        // Inventory 엔티티 설정
        modelBuilder.Entity<Inventory>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ProductCode)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.ProductName)
                .IsRequired()
                .HasMaxLength(200);

            // Optimistic Concurrency - RowVersion 설정
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasIndex(e => e.ProductCode).IsUnique();
        });
    }
}
