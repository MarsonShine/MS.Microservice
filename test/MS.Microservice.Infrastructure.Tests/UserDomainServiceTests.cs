using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;
using MS.Microservice.Domain.Consts;
using MS.Microservice.Domain.Exception;
using MS.Microservice.Domain.Services;
using NSubstitute;
using System.Linq.Expressions;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests
{
    /// <summary>
    /// 覆盖创建用户的函数式领域流程：
    /// 使用 Option 显式表达“用户已存在 / 用户不存在”的分支。
    /// </summary>
    public class UserDomainServiceTests
    {
        [Fact]
        public async Task CreateUserResultAsync_WhenUserAlreadyExists_ReturnsFailure()
        {
            var repository = Substitute.For<IUserRepository>();
            var service = new UserDomainService(repository);
            var existingUser = new User("demo", "Password123", "salt", false, "13800138000", 1, 1, "demo@example.com", "Demo", "", "")
            {
                Id = 1
            };

            repository.FindOptionAsync(Arg.Any<Expression<Func<User, bool>>>(), Arg.Any<CancellationToken>())
                .Returns((Option<User>)existingUser);

            var candidate = new User("demo", "Password123", "salt", false, "13800138001", 1, 1, "demo2@example.com", "Demo2", "", "");

            var result = await service.CreateUserResultAsync(candidate);

            Assert.True(result.IsFailure);
            Assert.Equal(ExceptionConsts.UserExisted, result.Error.Message);
        }

        [Fact]
        public async Task CreateUserResultAsync_WhenUserDoesNotExist_PersistsAndReturnsTrue()
        {
            var repository = Substitute.For<IUserRepository>();
            var unitOfWork = Substitute.For<IUnitOfWork>();
            repository.UnitOfWork.Returns(unitOfWork);
            repository.FindOptionAsync(Arg.Any<Expression<Func<User, bool>>>(), Arg.Any<CancellationToken>())
                .Returns((Option<User>)F.None);
            repository.InsertResultAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<User>());
            unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(1);

            var service = new UserDomainService(repository);
            var candidate = new User("demo", "Password123", "salt", false, "13800138000", 1, 1, "demo@example.com", "Demo", "", "");

            var result = await service.CreateUserResultAsync(candidate);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value);
            Assert.NotEqual("Password123", candidate.Password);
            await repository.Received(1).InsertResultAsync(candidate, Arg.Any<CancellationToken>());
            await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
