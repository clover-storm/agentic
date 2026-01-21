using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ConcurrencyPattern.Infrastructure.Redis.Services;

/// <summary>
/// Redis 연결 관리자
/// 연결 풀링 및 재연결 처리
/// </summary>
public interface IRedisConnectionManager : IDisposable
{
    IDatabase GetDatabase(int db = -1);
    ISubscriber GetSubscriber();
    IServer GetServer();
    bool IsConnected { get; }
}

public class RedisConnectionManager : IRedisConnectionManager
{
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisConnectionManager> _logger;
    private readonly Lazy<ConnectionMultiplexer> _connection;

    public RedisConnectionManager(
        IOptions<RedisSettings> settings,
        ILogger<RedisConnectionManager> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        _connection = new Lazy<ConnectionMultiplexer>(() =>
        {
            var options = ConfigurationOptions.Parse(_settings.ConnectionString);
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;
            options.ReconnectRetryPolicy = new ExponentialRetry(5000);

            var connection = ConnectionMultiplexer.Connect(options);

            connection.ConnectionFailed += (sender, args) =>
            {
                _logger.LogError(args.Exception, "Redis connection failed: {FailureType}", args.FailureType);
            };

            connection.ConnectionRestored += (sender, args) =>
            {
                _logger.LogInformation("Redis connection restored: {FailureType}", args.FailureType);
            };

            connection.ErrorMessage += (sender, args) =>
            {
                _logger.LogError("Redis error: {Message}", args.Message);
            };

            _logger.LogInformation("Redis connection established to {Endpoints}",
                string.Join(", ", connection.GetEndPoints().Select(e => e.ToString())));

            return connection;
        });
    }

    private ConnectionMultiplexer Connection => _connection.Value;

    public bool IsConnected => _connection.IsValueCreated && Connection.IsConnected;

    public IDatabase GetDatabase(int db = -1) => Connection.GetDatabase(db);

    public ISubscriber GetSubscriber() => Connection.GetSubscriber();

    public IServer GetServer()
    {
        var endpoint = Connection.GetEndPoints().First();
        return Connection.GetServer(endpoint);
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            Connection.Dispose();
        }
    }
}
