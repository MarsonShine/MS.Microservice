using NetArchTest.Rules;
using Xunit;

namespace MS.Microservice.Core.Tests.Architecture;

/// <summary>
/// 架构约束测试。
/// 验证项目间的依赖方向和分层边界符合 Clean Architecture。
/// 这些测试是护栏，防止依赖方向在重构中被意外打破。
/// </summary>
public class ArchitectureTests
{
    // =========================================================================
    // 依赖方向约束
    // =========================================================================

    [Fact]
    public void Domain_ShouldNot_DependOn_Infrastructure()
    {
        var domainAssembly = typeof(MS.Microservice.Domain.Identity.IdentityOptions).Assembly;

        var result = Types
            .InAssembly(domainAssembly)
            .That()
            .ResideInNamespace("MS.Microservice.Domain")
            .ShouldNot()
            .HaveDependencyOn("MS.Microservice.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain should not depend on Infrastructure. Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_ShouldNot_DependOn_Web()
    {
        var domainAssembly = typeof(MS.Microservice.Domain.Identity.IdentityOptions).Assembly;

        var result = Types
            .InAssembly(domainAssembly)
            .That()
            .ResideInNamespace("MS.Microservice.Domain")
            .ShouldNot()
            .HaveDependencyOn("MS.Microservice.Web")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain should not depend on Web. Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Core_ShouldNot_DependOn_Infrastructure()
    {
        var coreAssembly = typeof(MS.Microservice.Core.Extension.StringExtensions).Assembly;

        var result = Types
            .InAssembly(coreAssembly)
            .That()
            .ResideInNamespace("MS.Microservice.Core")
            .ShouldNot()
            .HaveDependencyOn("MS.Microservice.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Core should not depend on Infrastructure. Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Core_ShouldNot_DependOn_Web()
    {
        var coreAssembly = typeof(MS.Microservice.Core.Extension.StringExtensions).Assembly;

        var result = Types
            .InAssembly(coreAssembly)
            .That()
            .ResideInNamespace("MS.Microservice.Core")
            .ShouldNot()
            .HaveDependencyOn("MS.Microservice.Web")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Core should not depend on Web. Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Infrastructure_ShouldNot_DependOn_Web()
    {
        var infraAssembly = typeof(MS.Microservice.Infrastructure.DbContext.ActivationDbContext).Assembly;

        var result = Types
            .InAssembly(infraAssembly)
            .That()
            .ResideInNamespace("MS.Microservice.Infrastructure")
            .ShouldNot()
            .HaveDependencyOn("MS.Microservice.Web")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Infrastructure should not depend on Web. Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // =========================================================================
    // 命名空间归属约束
    // =========================================================================

    [Fact]
    public void WebApplicationTypes_ShouldResideIn_WebApplicationNamespace()
    {
        var webAssembly = typeof(MS.Microservice.Web.Controller.FeatureManagerController).Assembly;

        // Web/Application 下的类型不应再使用 Core 或 Domain 的命名空间
        var violatingTypes = Types
            .InAssembly(webAssembly)
            .That()
            .HaveNameEndingWith("Attribute")
            .And()
            .ResideInNamespace("MS.Microservice.Core.FeatureManager")
            .GetTypes();

        Assert.Empty(violatingTypes);
    }

    // =========================================================================
    // 领域层约束
    // =========================================================================

    [Fact]
    public void Domain_ShouldNot_Reference_OrmPackages()
    {
        var domainAssembly = typeof(MS.Microservice.Domain.Identity.IdentityOptions).Assembly;

        var result = Types
            .InAssembly(domainAssembly)
            .That()
            .ResideInNamespace("MS.Microservice.Domain")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain should not depend on EF Core. Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_ShouldNot_Reference_SqlSugar()
    {
        var domainAssembly = typeof(MS.Microservice.Domain.Identity.IdentityOptions).Assembly;

        var result = Types
            .InAssembly(domainAssembly)
            .That()
            .ResideInNamespace("MS.Microservice.Domain")
            .ShouldNot()
            .HaveDependencyOn("SqlSugar")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain should not depend on SqlSugar. Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_ShouldNot_Reference_Dapper()
    {
        var domainAssembly = typeof(MS.Microservice.Domain.Identity.IdentityOptions).Assembly;

        var result = Types
            .InAssembly(domainAssembly)
            .That()
            .ResideInNamespace("MS.Microservice.Domain")
            .ShouldNot()
            .HaveDependencyOn("Dapper")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain should not depend on Dapper. Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
