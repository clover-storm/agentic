using System.Text.Json;
using ConcurrencyPattern.Core.Commands;
using ConcurrencyPattern.Infrastructure.Redis.Services;
using FluentAssertions;
using Xunit;

namespace ConcurrencyPattern.Tests.Redis;

/// <summary>
/// RedisCommandEnvelope 직렬화/역직렬화 테스트
/// </summary>
public class RedisCommandEnvelopeTests
{
    [Fact]
    public void Create_ShouldCreateEnvelopeWithCorrectMetadata()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var command = new DepositCommand(accountId, 100m);
        var responseChannel = "test:response:123";

        // Act
        var envelope = RedisCommandEnvelope.Create(command, responseChannel);

        // Assert
        envelope.CommandId.Should().Be(command.CommandId.ToString());
        envelope.EntityType.Should().Be("Account");
        envelope.EntityId.Should().Be(accountId.ToString());
        envelope.ResponseChannel.Should().Be(responseChannel);
        envelope.CommandType.Should().Contain("DepositCommand");
        envelope.ResultType.Should().Contain("AccountCommandResult");
        envelope.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SerializeAndDeserialize_ShouldPreserveEnvelopeData()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var command = new DepositCommand(accountId, 250.50m);
        var envelope = RedisCommandEnvelope.Create(command, "test:response:456");

        // Act
        var json = JsonSerializer.Serialize(envelope);
        var restored = JsonSerializer.Deserialize<RedisCommandEnvelope>(json);

        // Assert
        restored.Should().NotBeNull();
        restored!.CommandId.Should().Be(envelope.CommandId);
        restored.EntityType.Should().Be(envelope.EntityType);
        restored.EntityId.Should().Be(envelope.EntityId);
        restored.ResponseChannel.Should().Be(envelope.ResponseChannel);
        restored.CommandData.Should().Be(envelope.CommandData);
    }

    [Fact]
    public void DeserializeCommand_ShouldRestoreOriginalCommand()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var originalAmount = 500m;
        var command = new DepositCommand(accountId, originalAmount);
        var envelope = RedisCommandEnvelope.Create(command, "test:response");

        // Simulate serialization round-trip
        var json = JsonSerializer.Serialize(envelope);
        var restoredEnvelope = JsonSerializer.Deserialize<RedisCommandEnvelope>(json);

        // Act
        var restoredCommand = restoredEnvelope!.DeserializeCommand() as DepositCommand;

        // Assert
        restoredCommand.Should().NotBeNull();
        restoredCommand!.EntityId.Should().Be(accountId);
        restoredCommand.Amount.Should().Be(originalAmount);
        restoredCommand.EntityType.Should().Be("Account");
    }

    [Fact]
    public void DeserializeCommand_WithWithdrawCommand_ShouldWork()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var amount = 75.25m;
        var command = new WithdrawCommand(accountId, amount);
        var envelope = RedisCommandEnvelope.Create(command, "test:response");

        var json = JsonSerializer.Serialize(envelope);
        var restoredEnvelope = JsonSerializer.Deserialize<RedisCommandEnvelope>(json);

        // Act
        var restoredCommand = restoredEnvelope!.DeserializeCommand() as WithdrawCommand;

        // Assert
        restoredCommand.Should().NotBeNull();
        restoredCommand!.EntityId.Should().Be(accountId);
        restoredCommand.Amount.Should().Be(amount);
    }
}

/// <summary>
/// RedisCommandResult 직렬화/역직렬화 테스트
/// </summary>
public class RedisCommandResultTests
{
    [Fact]
    public void FromSuccess_ShouldCreateSuccessResult()
    {
        // Arrange
        var commandId = Guid.NewGuid().ToString();
        var result = new AccountCommandResult
        {
            Success = true,
            AccountId = Guid.NewGuid(),
            NewBalance = 1500m,
            TransactionAmount = 500m
        };

        // Act
        var redisResult = RedisCommandResult.FromSuccess(commandId, result);

        // Assert
        redisResult.CommandId.Should().Be(commandId);
        redisResult.Success.Should().BeTrue();
        redisResult.ResultData.Should().NotBeNullOrEmpty();
        redisResult.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void FromError_ShouldCreateErrorResult()
    {
        // Arrange
        var commandId = Guid.NewGuid().ToString();
        var exception = new InvalidOperationException("Insufficient balance");

        // Act
        var redisResult = RedisCommandResult.FromError(commandId, exception);

        // Assert
        redisResult.CommandId.Should().Be(commandId);
        redisResult.Success.Should().BeFalse();
        redisResult.ErrorMessage.Should().Be("Insufficient balance");
        redisResult.ExceptionType.Should().Be("System.InvalidOperationException");
    }

    [Fact]
    public void DeserializeResult_ShouldRestoreOriginalResult()
    {
        // Arrange
        var commandId = Guid.NewGuid().ToString();
        var originalResult = new AccountCommandResult
        {
            Success = true,
            AccountId = Guid.NewGuid(),
            NewBalance = 2500m,
            TransactionAmount = 1000m
        };

        var redisResult = RedisCommandResult.FromSuccess(commandId, originalResult);

        // Simulate serialization round-trip
        var json = JsonSerializer.Serialize(redisResult);
        var restoredRedisResult = JsonSerializer.Deserialize<RedisCommandResult>(json);

        // Act
        var restoredResult = restoredRedisResult!.DeserializeResult<AccountCommandResult>();

        // Assert
        restoredResult.Should().NotBeNull();
        restoredResult!.Success.Should().Be(originalResult.Success);
        restoredResult.AccountId.Should().Be(originalResult.AccountId);
        restoredResult.NewBalance.Should().Be(originalResult.NewBalance);
        restoredResult.TransactionAmount.Should().Be(originalResult.TransactionAmount);
    }

    [Fact]
    public void DeserializeResult_WhenNotSuccess_ShouldReturnDefault()
    {
        // Arrange
        var commandId = Guid.NewGuid().ToString();
        var redisResult = RedisCommandResult.FromError(commandId, new Exception("Error"));

        // Act
        var result = redisResult.DeserializeResult<AccountCommandResult>();

        // Assert
        result.Should().BeNull();
    }
}
