using System.Text.Json;
using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Core.Entities;
using ConcurrencyPattern.Core.Interfaces;
using ConcurrencyPattern.Infrastructure.Data;
using ConcurrencyPattern.Infrastructure.Redis.Configuration;
using ConcurrencyPattern.Infrastructure.Redis.Services;
using ConcurrencyPattern.Infrastructure.Repositories;
using ConcurrencyPattern.SequentialProcessor.Handlers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace ConcurrencyPattern.Tests.Redis;

/// <summary>
/// Redis 통합 테스트 (Testcontainers 사용)
/// 실제 Redis를 사용하여 전체 플로우 검증
/// </summary>
public class RedisIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer;
    private IConnectionMultiplexer? _redis;
    private IDatabase? _db;

    public RedisIntegrationTests()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7.2-alpine")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _db = _redis.GetDatabase();
    }

    public async Task DisposeAsync()
    {
        if (_redis != null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
        }
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task ListPushPop_ShouldMaintainFIFOOrder()
    {
        // Arrange
        var queueKey = "test:queue:fifo";

        // Act - LPUSH로 3개 항목 추가 (왼쪽에 추가)
        await _db!.ListLeftPushAsync(queueKey, "first");
        await _db.ListLeftPushAsync(queueKey, "second");
        await _db.ListLeftPushAsync(queueKey, "third");

        // RPOP으로 꺼내면 FIFO 순서 (first가 먼저)
        var item1 = await _db.ListRightPopAsync(queueKey);
        var item2 = await _db.ListRightPopAsync(queueKey);
        var item3 = await _db.ListRightPopAsync(queueKey);

        // Assert
        item1.ToString().Should().Be("first");
        item2.ToString().Should().Be("second");
        item3.ToString().Should().Be("third");
    }

    [Fact]
    public async Task ListRightPop_WhenEmpty_ShouldReturnNull()
    {
        // Arrange
        var queueKey = "test:queue:empty";

        // Act
        var result = await _db!.ListRightPopAsync(queueKey);

        // Assert
        result.IsNull.Should().BeTrue();
    }

    [Fact]
    public async Task CommandEnvelope_ShouldBeSerializedAndDeserialized()
    {
        // Arrange
        var queueKey = "test:queue:envelope";
        var accountId = Guid.NewGuid();
        var command = new DepositCommand { AccountId = accountId, Amount = 100m };
        var envelope = RedisCommandEnvelope.Create(command, "test:response:123");

        // Act - Enqueue
        var envelopeJson = JsonSerializer.Serialize(envelope);
        await _db!.ListLeftPushAsync(queueKey, envelopeJson);

        // Dequeue
        var resultJson = await _db.ListRightPopAsync(queueKey);
        var resultEnvelope = JsonSerializer.Deserialize<RedisCommandEnvelope>(resultJson!);

        // Assert
        resultEnvelope.Should().NotBeNull();
        resultEnvelope!.CommandId.Should().Be(envelope.CommandId);
        resultEnvelope.EntityType.Should().Be("Account");
        resultEnvelope.EntityId.Should().Be(accountId.ToString());

        var restoredCommand = resultEnvelope.DeserializeCommand() as DepositCommand;
        restoredCommand.Should().NotBeNull();
        restoredCommand!.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task DifferentEntityQueues_ShouldBeIndependent()
    {
        // Arrange - 다른 엔티티별로 다른 큐
        var prefix = "test:queue:Account:";
        var entity1Queue = $"{prefix}{Guid.NewGuid()}";
        var entity2Queue = $"{prefix}{Guid.NewGuid()}";

        // Act - 각 큐에 독립적으로 추가
        await _db!.ListLeftPushAsync(entity1Queue, "entity1-item1");
        await _db.ListLeftPushAsync(entity1Queue, "entity1-item2");
        await _db.ListLeftPushAsync(entity2Queue, "entity2-item1");

        // Assert - 각 큐는 독립적
        var queue1Length = await _db.ListLengthAsync(entity1Queue);
        var queue2Length = await _db.ListLengthAsync(entity2Queue);

        queue1Length.Should().Be(2);
        queue2Length.Should().Be(1);

        // 각 큐에서 순서대로 꺼내기
        var e1Item1 = await _db.ListRightPopAsync(entity1Queue);
        var e2Item1 = await _db.ListRightPopAsync(entity2Queue);

        e1Item1.ToString().Should().Be("entity1-item1");
        e2Item1.ToString().Should().Be("entity2-item1");
    }

    [Fact]
    public async Task PubSub_ShouldDeliverMessages()
    {
        // Arrange
        var channel = "test:response:pubsub";
        var receivedMessage = "";
        var messageReceived = new TaskCompletionSource<bool>();

        var subscriber = _redis!.GetSubscriber();
        await subscriber.SubscribeAsync(RedisChannel.Literal(channel), (ch, msg) =>
        {
            receivedMessage = msg!;
            messageReceived.TrySetResult(true);
        });

        // Act
        await subscriber.PublishAsync(RedisChannel.Literal(channel), "test-message");

        // Assert
        var received = await Task.WhenAny(
            messageReceived.Task,
            Task.Delay(TimeSpan.FromSeconds(5)));

        received.Should().Be(messageReceived.Task);
        receivedMessage.Should().Be("test-message");
    }

    [Fact]
    public async Task CommandResult_ShouldBePublishedAndReceived()
    {
        // Arrange
        var commandId = Guid.NewGuid().ToString();
        var responseChannel = $"test:response:{commandId}";
        var receivedResult = new TaskCompletionSource<RedisCommandResult>();

        var subscriber = _redis!.GetSubscriber();
        await subscriber.SubscribeAsync(RedisChannel.Literal(responseChannel), (ch, msg) =>
        {
            var result = JsonSerializer.Deserialize<RedisCommandResult>(msg!);
            receivedResult.TrySetResult(result!);
        });

        // Act - 결과 발행
        var commandResult = new RedisCommandResult
        {
            CommandId = commandId,
            Success = true,
            ResultType = typeof(AccountCommandResult).AssemblyQualifiedName!,
            ResultData = JsonSerializer.Serialize(new AccountCommandResult
            {
                Success = true,
                NewBalance = 1500m
            })
        };

        await subscriber.PublishAsync(
            RedisChannel.Literal(responseChannel),
            JsonSerializer.Serialize(commandResult));

        // Assert
        var received = await Task.WhenAny(
            receivedResult.Task,
            Task.Delay(TimeSpan.FromSeconds(5)));

        received.Should().Be(receivedResult.Task);

        var result = await receivedResult.Task;
        result.CommandId.Should().Be(commandId);
        result.Success.Should().BeTrue();

        var accountResult = result.DeserializeResult<AccountCommandResult>();
        accountResult.Should().NotBeNull();
        accountResult!.NewBalance.Should().Be(1500m);
    }

    [Fact]
    public async Task KeyScan_ShouldFindAllContextQueues()
    {
        // Arrange - 여러 컨텍스트의 큐 생성
        var prefix = "scan:queue:";
        await _db!.ListLeftPushAsync($"{prefix}Account:1", "item");
        await _db.ListLeftPushAsync($"{prefix}Account:2", "item");
        await _db.ListLeftPushAsync($"{prefix}Inventory:A", "item");
        await _db.ListLeftPushAsync($"{prefix}Order:X", "item");

        // Act - 패턴으로 키 검색
        var server = _redis!.GetServer(_redis.GetEndPoints()[0]);
        var keys = new List<string>();

        await foreach (var key in server.KeysAsync(pattern: $"{prefix}*"))
        {
            keys.Add(key.ToString());
        }

        // Assert
        keys.Should().HaveCount(4);
        keys.Should().Contain(k => k.Contains("Account:1"));
        keys.Should().Contain(k => k.Contains("Account:2"));
        keys.Should().Contain(k => k.Contains("Inventory:A"));
        keys.Should().Contain(k => k.Contains("Order:X"));
    }

    [Fact]
    public async Task ContextExtraction_FromQueueKey_ShouldWork()
    {
        // Arrange
        var prefix = "extract:queue:";
        var keys = new[]
        {
            $"{prefix}Account:entity1",
            $"{prefix}Account:entity2",
            $"{prefix}Inventory:item1",
            $"{prefix}Order:order1"
        };

        foreach (var key in keys)
        {
            await _db!.ListLeftPushAsync(key, "item");
        }

        // Act - 컨텍스트 추출
        var server = _redis!.GetServer(_redis.GetEndPoints()[0]);
        var contexts = new HashSet<string>();

        await foreach (var key in server.KeysAsync(pattern: $"{prefix}*"))
        {
            var keyStr = key.ToString();
            var remainder = keyStr.Substring(prefix.Length);
            var colonIndex = remainder.IndexOf(':');
            if (colonIndex > 0)
            {
                var context = remainder.Substring(0, colonIndex);
                contexts.Add(context);
            }
        }

        // Assert
        contexts.Should().HaveCount(3);
        contexts.Should().Contain("Account");
        contexts.Should().Contain("Inventory");
        contexts.Should().Contain("Order");
    }
}

/// <summary>
/// 동시성 처리 통합 테스트
/// 실제 Redis와 함께 동시성 처리가 올바르게 동작하는지 검증
/// </summary>
public class RedisConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer;
    private IConnectionMultiplexer? _redis;
    private IDatabase? _db;

    public RedisConcurrencyIntegrationTests()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7.2-alpine")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _db = _redis.GetDatabase();
    }

    public async Task DisposeAsync()
    {
        if (_redis != null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
        }
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentPush_ToSameQueue_ShouldMaintainAllItems()
    {
        // Arrange
        var queueKey = "concurrent:queue:same";
        var itemCount = 100;

        // Act - 동시에 100개 항목 추가
        var tasks = Enumerable.Range(0, itemCount)
            .Select(i => _db!.ListLeftPushAsync(queueKey, $"item-{i}"))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        var length = await _db!.ListLengthAsync(queueKey);
        length.Should().Be(itemCount);
    }

    [Fact]
    public async Task ConcurrentPopFromSameQueue_ShouldReturnUniqueItems()
    {
        // Arrange
        var queueKey = "concurrent:queue:pop";
        var itemCount = 50;

        // 항목 추가
        for (int i = 0; i < itemCount; i++)
        {
            await _db!.ListLeftPushAsync(queueKey, $"item-{i}");
        }

        // Act - 동시에 pop
        var poppedItems = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, itemCount)
            .Select(async _ =>
            {
                var item = await _db!.ListRightPopAsync(queueKey);
                if (!item.IsNull)
                {
                    poppedItems.Add(item!);
                }
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - 모든 항목이 한 번씩만 pop됨 (순차 처리 보장)
        poppedItems.Should().HaveCount(itemCount);
        poppedItems.Distinct().Should().HaveCount(itemCount);
    }

    [Fact]
    public async Task ParallelPush_ToDifferentQueues_ShouldNotInterfere()
    {
        // Arrange
        var basePrefix = "parallel:queue:";
        var queueCount = 5;
        var itemsPerQueue = 20;

        // Act - 다른 큐에 동시에 추가
        var tasks = new List<Task>();
        for (int q = 0; q < queueCount; q++)
        {
            var queueKey = $"{basePrefix}{q}";
            for (int i = 0; i < itemsPerQueue; i++)
            {
                tasks.Add(_db!.ListLeftPushAsync(queueKey, $"item-{i}"));
            }
        }

        await Task.WhenAll(tasks);

        // Assert - 각 큐는 독립적으로 정확한 개수의 항목을 가짐
        for (int q = 0; q < queueCount; q++)
        {
            var queueKey = $"{basePrefix}{q}";
            var length = await _db!.ListLengthAsync(queueKey);
            length.Should().Be(itemsPerQueue);
        }
    }

    [Fact]
    public async Task SequentialProcessing_WithinSameQueue_ShouldPreserveOrder()
    {
        // Arrange
        var queueKey = "sequential:queue:order";
        var itemCount = 10;

        // 순서대로 추가
        for (int i = 0; i < itemCount; i++)
        {
            await _db!.ListLeftPushAsync(queueKey, $"item-{i}");
        }

        // Act - 순서대로 꺼내기
        var poppedItems = new List<string>();
        for (int i = 0; i < itemCount; i++)
        {
            var item = await _db!.ListRightPopAsync(queueKey);
            poppedItems.Add(item!);
        }

        // Assert - FIFO 순서 유지
        for (int i = 0; i < itemCount; i++)
        {
            poppedItems[i].Should().Be($"item-{i}");
        }
    }
}
