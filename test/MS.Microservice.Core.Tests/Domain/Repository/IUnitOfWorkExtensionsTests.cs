using System;
using System.Threading.Tasks;
using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Core.Domain.Repository.Extensions;
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

}
