using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using ConcurrencyPattern.Infrastructure.Redis.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ConcurrencyPattern.Tests.Redis;

/// <summary>
/// RedisListCommandQueue 단위 테스트
/// 큐 키 생성 및 기본 동작 테스트
/// </summary>
public class RedisListCommandQueueTests
{
    private readonly Mock<IRedisConnectionManager> _connectionManagerMock;
    private readonly Mock<ILogger<RedisListCommandQueue>> _loggerMock;
    private readonly IOptions<RedisSettings> _settings;

    public RedisListCommandQueueTests()
    {
        _connectionManagerMock = new Mock<IRedisConnectionManager>();
        _loggerMock = new Mock<ILogger<RedisListCommandQueue>>();
        _settings = Options.Create(new RedisSettings
        {
            ConnectionString = "localhost:6379",
            InstanceName = "test",
            CommandTimeoutSeconds = 30
        });
    }

    [Theory]
    [InlineData("Account", "123e4567-e89b-12d3-a456-426614174000")]
    [InlineData("Inventory", "987fcdeb-51a2-3b4c-d567-890123456789")]
    [InlineData("Order", "abcdef01-2345-6789-abcd-ef0123456789")]
    public void GetQueueKey_ShouldGenerateCorrectKey(string entityType, string entityIdStr)
    {
        // Arrange
        var entityId = Guid.Parse(entityIdStr);
        var queue = new RedisListCommandQueue(
            _connectionManagerMock.Object,
            _settings,
            _loggerMock.Object);

        // Act
        var queueKey = queue.GetQueueKey(entityType, entityId);

        // Assert
        queueKey.Should().Be($"test:queue:{entityType}:{entityId}");
    }

    [Fact]
    public void GetQueueKey_DifferentEntities_ShouldGenerateDifferentKeys()
    {
        // Arrange
        var queue = new RedisListCommandQueue(
            _connectionManagerMock.Object,
            _settings,
            _loggerMock.Object);

        var account1 = Guid.NewGuid();
        var account2 = Guid.NewGuid();
        var inventory1 = Guid.NewGuid();

        // Act
        var key1 = queue.GetQueueKey("Account", account1);
        var key2 = queue.GetQueueKey("Account", account2);
        var key3 = queue.GetQueueKey("Inventory", inventory1);

        // Assert
        key1.Should().NotBe(key2);
        key1.Should().NotBe(key3);
        key2.Should().NotBe(key3);
    }

    [Fact]
    public void GetQueueKey_SameEntity_ShouldGenerateSameKey()
    {
        // Arrange
        var queue = new RedisListCommandQueue(
            _connectionManagerMock.Object,
            _settings,
            _loggerMock.Object);

        var entityId = Guid.NewGuid();

        // Act
        var key1 = queue.GetQueueKey("Account", entityId);
        var key2 = queue.GetQueueKey("Account", entityId);

        // Assert
        key1.Should().Be(key2);
    }

    [Fact]
    public void Constructor_ShouldInitializeWithCorrectPrefix()
    {
        // Arrange & Act
        var queue = new RedisListCommandQueue(
            _connectionManagerMock.Object,
            _settings,
            _loggerMock.Object);

        var entityId = Guid.NewGuid();
        var key = queue.GetQueueKey("Test", entityId);

        // Assert
        key.Should().StartWith("test:queue:");
    }

    [Fact]
    public void EnqueueCompositeAsync_ShouldThrowNotSupportedException()
    {
        // Arrange
        var queue = new RedisListCommandQueue(
            _connectionManagerMock.Object,
            _settings,
            _loggerMock.Object);

        var entities = new List<(string EntityType, Guid EntityId)>
        {
            ("Account", Guid.NewGuid()),
            ("Account", Guid.NewGuid())
        };

        // Act & Assert
        var act = async () => await queue.EnqueueCompositeAsync<object>(
            null!,
            entities,
            CancellationToken.None);

        act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var subscriberMock = new Mock<ISubscriber>();
        _connectionManagerMock.Setup(x => x.GetSubscriber()).Returns(subscriberMock.Object);

        var queue = new RedisListCommandQueue(
            _connectionManagerMock.Object,
            _settings,
            _loggerMock.Object);

        // Act & Assert
        var act = () => queue.Dispose();
        act.Should().NotThrow();
    }
}

/// <summary>
/// 큐 키 패턴 검증 테스트
/// 컨텍스트별 분리가 올바르게 되는지 확인
/// </summary>
public class QueueKeyPatternTests
{
    [Fact]
    public void QueueKeys_ShouldFollowContextEntityPattern()
    {
        // 큐 키 패턴: {instanceName}:queue:{contextName}:{entityId}
        // 예: myapp:queue:Account:123e4567-e89b-12d3-a456-426614174000

        var settings = Options.Create(new RedisSettings { InstanceName = "myapp" });
        var connectionManagerMock = new Mock<IRedisConnectionManager>();
        var loggerMock = new Mock<ILogger<RedisListCommandQueue>>();

        var queue = new RedisListCommandQueue(
            connectionManagerMock.Object,
            settings,
            loggerMock.Object);

        // Act
        var accountId = Guid.Parse("123e4567-e89b-12d3-a456-426614174000");
        var key = queue.GetQueueKey("Account", accountId);

        // Assert
        key.Should().Be("myapp:queue:Account:123e4567-e89b-12d3-a456-426614174000");

        // 패턴 분해 검증
        var parts = key.Split(':');
        parts.Should().HaveCount(4);
        parts[0].Should().Be("myapp");      // instance name
        parts[1].Should().Be("queue");      // fixed prefix
        parts[2].Should().Be("Account");    // context (entity type)
        parts[3].Should().Be("123e4567-e89b-12d3-a456-426614174000"); // entity id
    }

    [Fact]
    public void DifferentContexts_ShouldHaveDifferentKeyPrefixes()
    {
        // 컨텍스트별 병렬 처리를 위해 다른 컨텍스트는 다른 키 prefix를 가져야 함
        var settings = Options.Create(new RedisSettings { InstanceName = "app" });
        var connectionManagerMock = new Mock<IRedisConnectionManager>();
        var loggerMock = new Mock<ILogger<RedisListCommandQueue>>();

        var queue = new RedisListCommandQueue(
            connectionManagerMock.Object,
            settings,
            loggerMock.Object);

        var entityId = Guid.NewGuid();

        // Act
        var accountKey = queue.GetQueueKey("Account", entityId);
        var inventoryKey = queue.GetQueueKey("Inventory", entityId);
        var orderKey = queue.GetQueueKey("Order", entityId);

        // Assert - 같은 entityId여도 컨텍스트가 다르면 다른 키
        accountKey.Should().Contain(":Account:");
        inventoryKey.Should().Contain(":Inventory:");
        orderKey.Should().Contain(":Order:");

        accountKey.Should().NotBe(inventoryKey);
        inventoryKey.Should().NotBe(orderKey);
    }
}
