using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Application.Users;
using MS.Microservice.Web.Infrastructure.Applications.Users;
using System.Text;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional
{
    /// <summary>
    /// 验证创建用户示例中的纯函数式转换逻辑。
    /// 这些测试不依赖 Web 宿主，只验证命令校验、角色检查和 DTO -> 聚合映射。
    /// </summary>
    public class UserCreatedCommandExtensionsTests
    {
        [Fact]
        public void EnsureRolesExist_WhenAllRolesExist_ReturnsSome()
        {
            var command = CreateCommand([new RoleDto { Id = 1, Name = "Admin" }]);
            var roles = new List<Role> { new(1, "Admin", "管理员") };

            Option<UserCreatedCommand> result = command.EnsureRolesExist(roles);

            Assert.True(result.IsSome);
        }

        [Fact]
        public void EnsureRolesExist_WhenRoleMissing_ReturnsNone()
        {
            var command = CreateCommand([new RoleDto { Id = 9, Name = "Ghost" }]);
            var roles = new List<Role> { new(1, "Admin", "管理员") };

            Option<UserCreatedCommand> result = command.EnsureRolesExist(roles);

            Assert.True(result.IsNone);
        }

        [Fact]
        public void ToDomainUser_MapsCommandIntoAggregate()
        {
            var command = CreateCommand([new RoleDto { Id = 1, Name = "Admin" }]);
            var currentUser = new CurrentUser(7, "creator", "creator@demo.local", "13800138000", []);

            var user = command.ToDomainUser(currentUser, "salt-value");

            Assert.Equal("demo-account", user.Account);
            Assert.Equal("salt-value", user.Salt);
            Assert.Equal("示例用户", user.Name);
            Assert.Equal("13800138000", user.Telephone);
            Assert.Equal("demo@example.com", user.Email);
            Assert.Single(user.Roles);
            Assert.Equal(1, user.Roles.Single().Id);
        }

        [Fact]
        public async Task ValidateAsync_WhenCommandIsValid_ReturnsNone()
        {
            var command = CreateCommand();

            Option<string> result = await command.ValidateAsync();

            Assert.True(result.IsNone);
        }

        [Fact]
        public async Task ValidateResultAsync_WhenCommandIsValid_ReturnsSuccess()
        {
            var command = CreateCommand();

            var result = await command.ValidateResultAsync();

            Assert.True(result.IsSuccess);
            Assert.Same(command, result.Value);
        }

        [Fact]
        public void EnsureRolesExistResult_WhenRoleMissing_ReturnsFailure()
        {
            var command = CreateCommand([new RoleDto { Id = 9, Name = "Ghost" }]);
            var roles = new List<Role> { new(1, "Admin", "管理员") };

            var result = command.EnsureRolesExistResult(roles);

            Assert.True(result.IsFailure);
            Assert.Equal("错误的角色参数", result.Error.Message);
        }

        [Fact]
        public void ToDomainUserResult_WhenPasswordIsNotBase64_ReturnsFailure()
        {
            var command = new UserCreatedCommand(
                account: "demo-account",
                userName: "示例用户",
                passowrd: "not-base64",
                telephone: "13800138000",
                email: "demo@example.com",
                roles: []);
            var currentUser = new CurrentUser(7, "creator", "creator@demo.local", "13800138000", []);

            var result = command.ToDomainUserResult(currentUser, "salt-value");

            Assert.True(result.IsFailure);
        }

        private static UserCreatedCommand CreateCommand(List<RoleDto>? roles = null)
        {
            var rawPassword = "Password123";
            var encodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawPassword));

            return new UserCreatedCommand(
                account: "demo-account",
                userName: "示例用户",
                passowrd: encodedPassword,
                telephone: "13800138000",
                email: "demo@example.com",
                roles: roles ?? []);
        }
    }
}
