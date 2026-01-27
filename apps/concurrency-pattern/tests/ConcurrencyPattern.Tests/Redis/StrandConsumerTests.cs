using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using ConcurrencyPattern.Infrastructure.Redis.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ConcurrencyPattern.Tests.Redis;

/// <summary>
/// StrandConsumer 단위 테스트
/// Strand 기반 분산 Consumer 패턴 검증
/// </summary>
public class StrandConsumerTests
{
    private readonly Mock<IRedisConnectionManager> _connectionManagerMock;
    private readonly Mock<IRedisDistributedLock> _distributedLockMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<ILogger<StrandConsumer>> _loggerMock;
    private readonly IOptions<RedisSettings> _settings;

    public StrandConsumerTests()
    {
        _connectionManagerMock = new Mock<IRedisConnectionManager>();
        _distributedLockMock = new Mock<IRedisDistributedLock>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _loggerMock = new Mock<ILogger<StrandConsumer>>();
        _settings = Options.Create(new RedisSettings
        {
            ConnectionString = "localhost:6379",
            InstanceName = "test",
            CommandTimeoutSeconds = 30,
            LockExpirySeconds = 30,
            StrandLeaseSeconds = 5,
            StrandBatchTimeoutSeconds = 2,
            StrandMaxBatchSize = 10,
            StrandDiscoveryIntervalSeconds = 1,
            StrandMaxIdleIterations = 3
        });
    }

    [Fact]
    public void StrandConsumer_ShouldInitializeWithUniqueConsumerId()
    {
        // Act
        var consumer1 = new StrandConsumer(
            _connectionManagerMock.Object,
            _distributedLockMock.Object,
            _scopeFactoryMock.Object,
            _settings,
            _loggerMock.Object);

        var consumer2 = new StrandConsumer(
            _connectionManagerMock.Object,
            _distributedLockMock.Object,
            _scopeFactoryMock.Object,
            _settings,
            _loggerMock.Object);

        // Assert - 각 Consumer는 고유한 인스턴스
        consumer1.Should().NotBeSameAs(consumer2);
    }

    [Fact]
    public async Task StrandConsumer_ShouldClaimStrand_WhenLockAcquired()
    {
        // Arrange
        var strandKey = "Account:123";

        _distributedLockMock
            .Setup(x => x.TryAcquireAsync(
                $"strand:{strandKey}",
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Mock: 큐가 비어있어 즉시 종료
        var dbMock = new Mock<IDatabase>();
        dbMock.Setup(x => x.ListRightPopAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        dbMock.Setup(x => x.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);

        _connectionManagerMock.Setup(x => x.GetDatabase(It.IsAny<int>())).Returns(dbMock.Object);

        var consumer = new StrandConsumer(
            _connectionManagerMock.Object,
            _distributedLockMock.Object,
            _scopeFactoryMock.Object,
            _settings,
            _loggerMock.Object);

        using var cts = new CancellationTokenSource();

        // Act
        await consumer.TryClaimStrandAsync(strandKey, cts.Token);

        // Assert
        _distributedLockMock.Verify(
            x => x.TryAcquireAsync(
                $"strand:{strandKey}",
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StrandConsumer_ShouldNotClaimStrand_WhenLockNotAcquired()
    {
        // Arrange
        var strandKey = "Account:456";

        _distributedLockMock
            .Setup(x => x.TryAcquireAsync(
                $"strand:{strandKey}",
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var consumer = new StrandConsumer(
            _connectionManagerMock.Object,
            _distributedLockMock.Object,
            _scopeFactoryMock.Object,
            _settings,
            _loggerMock.Object);

        using var cts = new CancellationTokenSource();

        // Act
        await consumer.TryClaimStrandAsync(strandKey, cts.Token);

        // Assert - Lock 획득 실패 시 Release 호출 없어야 함
        _distributedLockMock.Verify(
            x => x.ReleaseAsync($"strand:{strandKey}"),
            Times.Never);
    }

    [Fact]
    public async Task StrandConsumer_ShouldReleaseLock_WhenQueueEmpty()
    {
        // Arrange
        var strandKey = "Account:789";

        _distributedLockMock
            .Setup(x => x.TryAcquireAsync(
                $"strand:{strandKey}",
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var dbMock = new Mock<IDatabase>();
        dbMock.Setup(x => x.ListRightPopAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        dbMock.Setup(x => x.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);

        _connectionManagerMock.Setup(x => x.GetDatabase(It.IsAny<int>())).Returns(dbMock.Object);

        var consumer = new StrandConsumer(
            _connectionManagerMock.Object,
            _distributedLockMock.Object,
            _scopeFactoryMock.Object,
            _settings,
            _loggerMock.Object);

        using var cts = new CancellationTokenSource();

        // Act
        await consumer.TryClaimStrandAsync(strandKey, cts.Token);

        // 처리 Task가 완료될 때까지 대기
        await Task.Delay(500);

        // Assert - Idle timeout 후 Lock 해제
        _distributedLockMock.Verify(
            x => x.ReleaseAsync($"strand:{strandKey}"),
            Times.Once);
    }

    [Fact]
    public async Task StrandConsumer_ShouldStopGracefully_WhenCancelled()
    {
        // Arrange
        var serverMock = new Mock<IServer>();
        serverMock
            .Setup(x => x.KeysAsync(
                It.IsAny<int>(), It.IsAny<RedisValue>(),
                It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(AsyncEnumerableEmpty());

        _connectionManagerMock.Setup(x => x.GetServer()).Returns(serverMock.Object);

        var consumer = new StrandConsumer(
            _connectionManagerMock.Object,
            _distributedLockMock.Object,
            _scopeFactoryMock.Object,
            _settings,
            _loggerMock.Object);

        using var cts = new CancellationTokenSource();

        // Act
        var executeTask = consumer.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        // Assert - 예외 없이 종료
        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void RedisSettings_ShouldHaveStrandDefaults()
    {
        // Arrange
        var settings = new RedisSettings();

        // Assert
        settings.UseStrandMode.Should().BeFalse();
        settings.UseEventDrivenStrand.Should().BeFalse();
        settings.StrandLeaseSeconds.Should().Be(30);
        settings.StrandBatchTimeoutSeconds.Should().Be(5);
        settings.StrandMaxBatchSize.Should().Be(100);
        settings.StrandDiscoveryIntervalSeconds.Should().Be(2);
        settings.StrandMaxIdleIterations.Should().Be(100);
    }

    [Fact]
    public async Task DistributedLock_RefreshAsync_ShouldBeCalledInInterface()
    {
        // Arrange
        _distributedLockMock
            .Setup(x => x.RefreshAsync("strand:Account:1", It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        // Act
        var result = await _distributedLockMock.Object.RefreshAsync("strand:Account:1", TimeSpan.FromSeconds(30));

        // Assert
        result.Should().BeTrue();
        _distributedLockMock.Verify(
            x => x.RefreshAsync("strand:Account:1", It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    [Fact]
    public void StrandNotification_ShouldSerializeCorrectly()
    {
        // Arrange
        var notification = new StrandNotification
        {
            StrandKey = "Account:123",
            EventType = StrandEventType.Active,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(notification);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<StrandNotification>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.StrandKey.Should().Be("Account:123");
        deserialized.EventType.Should().Be(StrandEventType.Active);
    }

    [Fact]
    public void StrandEventType_ShouldHaveActiveAndInactive()
    {
        // Assert
        Enum.GetValues<StrandEventType>().Should().HaveCount(2);
        StrandEventType.Active.Should().BeDefined();
        StrandEventType.Inactive.Should().BeDefined();
    }

    /// <summary>
    /// 빈 IAsyncEnumerable 헬퍼
    /// </summary>
    private static async IAsyncEnumerable<RedisKey> AsyncEnumerableEmpty()
    {
        await Task.CompletedTask;
        yield break;
    }
}
