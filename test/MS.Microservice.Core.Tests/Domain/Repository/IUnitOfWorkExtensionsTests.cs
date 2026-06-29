using System;
using System.Threading.Tasks;
using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Core.Domain.Repository.Extensions;
using MS.Microservice.Core.Domain.Repository.SqlSugar;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Functional;
using NSubstitute;
using Xunit;

namespace MS.Microservice.Core.Tests.Domain.Repository;

public sealed class IUnitOfWorkExtensionsTests
{
    [Fact]
    public async Task SaveChangesEitherAsync_WhenSaveChangesThrows_ShouldReturnLeft()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(default).Returns(_ => Task.FromException<int>(new InvalidOperationException("boom")));

        var result = await unitOfWork.SaveChangesEitherAsync();

        Assert.True(result.IsLeft);
        Assert.Equal("persistence.save_changes", result.Left.Code);
    }

    [Fact]
    public async Task SaveEntitiesEitherAsync_WhenSaveEntitiesReturnsFalse_ShouldReturnUnexpectedLeft()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveEntitiesAsync(default).Returns(false);

        var result = await unitOfWork.SaveEntitiesEitherAsync();

        Assert.True(result.IsLeft);
        Assert.Equal("unexpected", result.Left.Code);
    }

    [Fact]
    public async Task SaveEntitiesResultAsync_WhenSaveEntitiesReturnsFalse_ShouldReturnFailure()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveEntitiesAsync(default).Returns(false);

        Result<bool> result = await unitOfWork.SaveEntitiesResultAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task UnitOfWorkAsync_WhenExecutionSucceeds_ShouldCommit()
    {
        var unitOfWork = Substitute.For<ISqlSugarUnitOfWork>();

        int result = await unitOfWork.UnitOfWorkAsync(async () =>
        {
            await Task.CompletedTask;
            return 42;
        });

        Assert.Equal(42, result);
        await unitOfWork.Received(1).BeginAsync();
        await unitOfWork.Received(1).CommitAsync();
        await unitOfWork.DidNotReceive().RollbackAsync();
    }

    [Fact]
    public async Task UnitOfWorkAsync_WhenExecutionThrows_ShouldRollback()
    {
        var unitOfWork = Substitute.For<ISqlSugarUnitOfWork>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.UnitOfWorkAsync(async () =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("boom");
            }));

        await unitOfWork.Received(1).BeginAsync();
        await unitOfWork.Received(1).RollbackAsync();
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task UnitOfWorkEitherAsync_WhenBusinessReturnsLeft_ShouldRollback()
    {
        var unitOfWork = Substitute.For<ISqlSugarUnitOfWork>();

        var result = await unitOfWork.UnitOfWorkEitherAsync(async () =>
        {
            await Task.CompletedTask;
            return (Either<Error, int>)F.Left(Error.Validation("invalid"));
        });

        Assert.True(result.IsLeft);
        Assert.Equal("validation", result.Left.Code);
        await unitOfWork.Received(1).RollbackAsync();
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task UnitOfWorkResultAsync_WhenResultFails_ShouldRollback()
    {
        var unitOfWork = Substitute.For<ISqlSugarUnitOfWork>();

        Result<int> result = await unitOfWork.UnitOfWorkResultAsync(async () =>
        {
            await Task.CompletedTask;
            return Result<int>.Fail(new InvalidOperationException("invalid"));
        });

        Assert.True(result.IsFailure);
        await unitOfWork.Received(1).RollbackAsync();
        await unitOfWork.DidNotReceive().CommitAsync();
    }
}
