using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// Redis 기반 분산 락
/// Redlock 알고리즘 단순 구현
/// </summary>
public interface IRedisDistributedLock
{
    Task<IAsyncDisposable?> AcquireAsync(string resource, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<bool> TryAcquireAsync(string resource, TimeSpan? expiry = null, CancellationToken ct = default);
    Task ReleaseAsync(string resource);

    /// <summary>
    /// Lock Lease 원자적 갱신 (소유자 확인 후 TTL 연장)
    /// Strand Consumer의 Lock 유지에 사용
    /// </summary>
    Task<bool> RefreshAsync(string resource, TimeSpan? expiry = null);
}

public class RedisDistributedLock : IRedisDistributedLock
{
    private readonly IRedisConnectionManager _connectionManager;
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisDistributedLock> _logger;
    private readonly string _lockPrefix;

    // 락 소유자 식별을 위한 고유 값
    private readonly string _lockValue = $"{Environment.MachineName}:{Guid.NewGuid()}";

    public RedisDistributedLock(
        IRedisConnectionManager connectionManager,
        IOptions<RedisSettings> settings,
        ILogger<RedisDistributedLock> logger)
    {
        _connectionManager = connectionManager;
        _settings = settings.Value;
        _logger = logger;
        _lockPrefix = $"{_settings.InstanceName}:lock:";
    }

    /// <summary>
    /// 락 획득 시도 (대기)
    /// </summary>
    public async Task<IAsyncDisposable?> AcquireAsync(
        string resource,
        TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        var lockKey = $"{_lockPrefix}{resource}";
        var lockExpiry = expiry ?? TimeSpan.FromSeconds(_settings.LockExpirySeconds);
        var db = _connectionManager.GetDatabase();

        var retryDelay = TimeSpan.FromMilliseconds(50);
        var maxRetries = 100; // 최대 5초 대기

        for (int i = 0; i < maxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (await TryAcquireLockAsync(db, lockKey, lockExpiry))
            {
                _logger.LogDebug("Lock acquired: {Resource}", resource);
                return new LockHandle(this, resource);
            }

            await Task.Delay(retryDelay, ct);
        }

        _logger.LogWarning("Failed to acquire lock after retries: {Resource}", resource);
        return null;
    }

    /// <summary>
    /// 락 획득 시도 (즉시 반환)
    /// </summary>
    public async Task<bool> TryAcquireAsync(
        string resource,
        TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        var lockKey = $"{_lockPrefix}{resource}";
        var lockExpiry = expiry ?? TimeSpan.FromSeconds(_settings.LockExpirySeconds);
        var db = _connectionManager.GetDatabase();

        var result = await TryAcquireLockAsync(db, lockKey, lockExpiry);
        if (result)
        {
            _logger.LogDebug("Lock acquired: {Resource}", resource);
        }
        return result;
    }

    /// <summary>
    /// 락 해제
    /// </summary>
    public async Task ReleaseAsync(string resource)
    {
        var lockKey = $"{_lockPrefix}{resource}";
        var db = _connectionManager.GetDatabase();

        // Lua 스크립트로 원자적 해제 (자신이 소유한 락만 해제)
        var script = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end";

        var result = await db.ScriptEvaluateAsync(script,
            new RedisKey[] { lockKey },
            new RedisValue[] { _lockValue });

        if ((int)result == 1)
        {
            _logger.LogDebug("Lock released: {Resource}", resource);
        }
        else
        {
            _logger.LogWarning("Lock release failed (not owner or expired): {Resource}", resource);
        }
    }

    /// <summary>
    /// Lock TTL 원자적 갱신 (Lua 스크립트: 소유자 확인 후 PEXPIRE)
    /// Release-Reacquire 방식의 Race Condition 제거
    /// </summary>
    public async Task<bool> RefreshAsync(string resource, TimeSpan? expiry = null)
    {
        var lockKey = $"{_lockPrefix}{resource}";
        var lockExpiry = expiry ?? TimeSpan.FromSeconds(_settings.LockExpirySeconds);
        var db = _connectionManager.GetDatabase();

        var script = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('pexpire', KEYS[1], ARGV[2])
            else
                return 0
            end";

        var result = await db.ScriptEvaluateAsync(script,
            new RedisKey[] { lockKey },
            new RedisValue[] { _lockValue, (long)lockExpiry.TotalMilliseconds });

        var refreshed = (int)result == 1;
        if (refreshed)
        {
            _logger.LogDebug("Lock refreshed: {Resource}, TTL={TTL}ms", resource, lockExpiry.TotalMilliseconds);
        }
        else
        {
            _logger.LogWarning("Lock refresh failed (not owner or expired): {Resource}", resource);
        }

        return refreshed;
    }

    private async Task<bool> TryAcquireLockAsync(IDatabase db, string lockKey, TimeSpan expiry)
    {
        // SET NX EX - 존재하지 않을 때만 설정하고 만료 시간 지정
        return await db.StringSetAsync(lockKey, _lockValue, expiry, When.NotExists);
    }

    /// <summary>
    /// 락 핸들 (IAsyncDisposable)
    /// </summary>
    private class LockHandle : IAsyncDisposable
    {
        private readonly RedisDistributedLock _lock;
        private readonly string _resource;
        private bool _disposed;

        public LockHandle(RedisDistributedLock @lock, string resource)
        {
            _lock = @lock;
            _resource = resource;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await _lock.ReleaseAsync(_resource);
            }
        }
    }
}
