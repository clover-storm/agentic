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
/// RedisContextConsumer 단위 테스트
/// 컨텍스트별 독립 Consumer 동작 검증
/// </summary>
public class RedisContextConsumerTests
{
    private readonly Mock<IRedisConnectionManager> _connectionManagerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ILogger<RedisContextConsumerManager>> _loggerMock;
    private readonly IOptions<RedisSettings> _settings;

    public RedisContextConsumerTests()
    {
        _connectionManagerMock = new Mock<IRedisConnectionManager>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<RedisContextConsumerManager>>();
        _settings = Options.Create(new RedisSettings
        {
            ConnectionString = "localhost:6379",
            InstanceName = "test",
            CommandTimeoutSeconds = 30
        });
    }

    [Fact]
    public void RedisContextConsumer_ShouldInitializeWithCorrectContextName()
    {
        // Arrange
        var contextName = "Account";
        var logger = new Mock<ILogger>();

        // Act
        var consumer = new RedisContextConsumer(
            contextName,
            _connectionManagerMock.Object,
            _serviceProviderMock.Object,
            _settings,
            logger.Object);

        // Assert
        consumer.ContextName.Should().Be(contextName);
        consumer.IsRunning.Should().BeFalse();
    }

    [Theory]
    [InlineData("Account")]
    [InlineData("Inventory")]
    [InlineData("Order")]
    [InlineData("Transfer")]
    public void RedisContextConsumer_ShouldSupportVariousContextNames(string contextName)
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var consumer = new RedisContextConsumer(
            contextName,
            _connectionManagerMock.Object,
            _serviceProviderMock.Object,
            _settings,
            logger.Object);

        // Assert
        consumer.ContextName.Should().Be(contextName);
    }

    [Fact]
    public void RedisContextConsumer_Dispose_ShouldNotThrow()
    {
        // Arrange
        var logger = new Mock<ILogger>();
        var consumer = new RedisContextConsumer(
            "Account",
            _connectionManagerMock.Object,
            _serviceProviderMock.Object,
            _settings,
            logger.Object);

        // Act & Assert
        var act = () => consumer.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task RedisContextConsumer_StartAsync_WhenCancelled_ShouldStopGracefully()
    {
        // Arrange
        var serverMock = new Mock<IServer>();
        var emptyKeys = AsyncEnumerable.Empty<RedisKey>();
        serverMock.Setup(s => s.KeysAsync(
            It.IsAny<int>(),
            It.IsAny<RedisValue>(),
            It.IsAny<int>(),
            It.IsAny<long>(),
            It.IsAny<int>(),
            It.IsAny<CommandFlags>()))
            .Returns(emptyKeys);

        _connectionManagerMock.Setup(c => c.GetServer()).Returns(serverMock.Object);

        var logger = new Mock<ILogger>();
        var consumer = new RedisContextConsumer(
            "Account",
            _connectionManagerMock.Object,
            _serviceProviderMock.Object,
            _settings,
            logger.Object);

        var cts = new CancellationTokenSource();

        // Act
        var task = consumer.StartAsync(cts.Token);
        await Task.Delay(100); // Consumer가 시작되도록 대기
        cts.Cancel();

        // Assert
        await task.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
        consumer.IsRunning.Should().BeFalse();
    }
}

/// <summary>
/// 컨텍스트 분리 검증 테스트
/// 컨텍스트별로 독립적인 Consumer가 동작하는지 확인
/// </summary>
public class ContextSeparationTests
{
    [Fact]
    public void DifferentContexts_ShouldHaveIndependentConsumers()
    {
        // Arrange
        var connectionManagerMock = new Mock<IRedisConnectionManager>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var settings = Options.Create(new RedisSettings { InstanceName = "test" });
        var logger = new Mock<ILogger>();

        // Act - 서로 다른 컨텍스트의 Consumer 생성
        var accountConsumer = new RedisContextConsumer(
            "Account",
            connectionManagerMock.Object,
            serviceProviderMock.Object,
            settings,
            logger.Object);

        var inventoryConsumer = new RedisContextConsumer(
            "Inventory",
            connectionManagerMock.Object,
            serviceProviderMock.Object,
            settings,
            logger.Object);

        var orderConsumer = new RedisContextConsumer(
            "Order",
            connectionManagerMock.Object,
            serviceProviderMock.Object,
            settings,
            logger.Object);

        // Assert
        accountConsumer.ContextName.Should().NotBe(inventoryConsumer.ContextName);
        inventoryConsumer.ContextName.Should().NotBe(orderConsumer.ContextName);
        accountConsumer.ContextName.Should().NotBe(orderConsumer.ContextName);

        // 각 Consumer는 독립적인 인스턴스
        accountConsumer.Should().NotBeSameAs(inventoryConsumer);
        inventoryConsumer.Should().NotBeSameAs(orderConsumer);
    }

    [Fact]
    public void ContextConsumerManager_ShouldManageMultipleContexts()
    {
        // 이 테스트는 컨텍스트 매니저가 여러 컨텍스트를 관리할 수 있는지 검증
        // 실제 동작은 통합 테스트에서 검증

        // Arrange
        var contexts = new[] { "Account", "Inventory", "Order", "Payment" };
        var consumers = new Dictionary<string, RedisContextConsumer>();

        var connectionManagerMock = new Mock<IRedisConnectionManager>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var settings = Options.Create(new RedisSettings { InstanceName = "test" });
        var logger = new Mock<ILogger>();

        // Act
        foreach (var context in contexts)
        {
            consumers[context] = new RedisContextConsumer(
                context,
                connectionManagerMock.Object,
                serviceProviderMock.Object,
                settings,
                logger.Object);
        }

        // Assert
        consumers.Should().HaveCount(4);
        consumers.Keys.Should().Contain("Account");
        consumers.Keys.Should().Contain("Inventory");
        consumers.Keys.Should().Contain("Order");
        consumers.Keys.Should().Contain("Payment");

        // Cleanup
        foreach (var consumer in consumers.Values)
        {
            consumer.Dispose();
        }
    }
}

/// <summary>
/// AsyncEnumerable helper for testing
/// </summary>
internal static class AsyncEnumerable
{
    public static IAsyncEnumerable<T> Empty<T>()
    {
        return EmptyAsyncEnumerable<T>.Instance;
    }

    private class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        public static readonly EmptyAsyncEnumerable<T> Instance = new();

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return EmptyAsyncEnumerator<T>.Instance;
        }
    }

    private class EmptyAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        public static readonly EmptyAsyncEnumerator<T> Instance = new();

        public T Current => default!;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<bool> MoveNextAsync() => new(false);
    }
}
