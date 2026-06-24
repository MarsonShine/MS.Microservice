using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Domain;
using MS.Microservice.Web.Controller;
using NetArchTest.Rules;

namespace MS.Microservice.Core.Tests.Architecture;

public sealed class LayerDependencyTests
{
    [Theory]
    [InlineData("MS.Microservice.Infrastructure")]
    [InlineData("MS.Microservice.Persistence.EFCore")]
    [InlineData("MS.Microservice.Persistence.SqlSugar")]
    [InlineData("MS.Microservice.Web")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("SqlSugar")]
    public void Domain_Should_Not_Depend_On_Infrastructure_Or_Web_Technologies(string dependency)
    {
        var result = Types
            .InAssembly(typeof(Entity).Assembly)
            .ShouldNot()
            .HaveDependencyOn(dependency)
            .GetResult();

        result.IsSuccessful.Should().BeTrue($"Domain must not depend on {dependency}.");
    }

    [Theory]
    [InlineData("MS.Microservice.Web")]
    public void Infrastructure_Should_Not_Depend_On_Web(string dependency)
    {
        var result = Types
            .InAssembly(typeof(InfrastructureServiceCollectionExtensions).Assembly)
            .ShouldNot()
            .HaveDependencyOn(dependency)
            .GetResult();

        result.IsSuccessful.Should().BeTrue($"Infrastructure must not depend on {dependency}.");
    }

    [Theory]
    [InlineData("MS.Microservice.Infrastructure.Repository")]
    [InlineData("Dapper")]
    [InlineData("System.Data")]
    public void Controllers_Should_Not_Depend_On_Repositories_Or_Direct_Sql(string dependency)
    {
        var result = Types
            .InAssembly(typeof(UserController).Assembly)
            .That()
            .ResideInNamespace("MS.Microservice.Web.Controller")
            .ShouldNot()
            .HaveDependencyOn(dependency)
            .GetResult();

        result.IsSuccessful.Should().BeTrue($"Controllers must not depend on {dependency}.");
    }
}
