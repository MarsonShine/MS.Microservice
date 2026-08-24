using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Web.Application.Models;
using System.Reflection;
using System.Text.Json;

namespace MS.Microservice.Core.Tests.Architecture;

public sealed class PublicNamingCompatibilityTests
{
    [Fact]
    public void UpdaterContracts_ExposeCorrectedNames()
    {
        Assert.NotNull(typeof(IUpdater<>).GetProperty("UpdaterId"));
        Assert.NotNull(typeof(ICreatorAndUpdater<>));
        Assert.NotNull(typeof(User).GetProperty("UpdaterId"));
        Assert.NotNull(typeof(UserPagedResponse).GetProperty("UpdaterId"));
    }

    [Fact]
    public void LegacyUpdatorNames_AreObsoleteCompatibilityShims()
    {
        var primitivesAssembly = typeof(IUpdater<>).Assembly;
        var legacyUpdater = primitivesAssembly.GetType(
            "MS.Microservice.Core.Domain.Entity.IUpdator`1",
            throwOnError: true)!;
        var legacyCreatorUpdater = primitivesAssembly.GetType(
            "MS.Microservice.Core.Domain.Entity.ICreatorAndUpdator`1",
            throwOnError: true)!;

        AssertObsolete(legacyUpdater);
        AssertObsolete(legacyCreatorUpdater);
        AssertObsolete(typeof(User).GetProperty("UpdatorId")!);
        AssertObsolete(typeof(UserPagedResponse).GetProperty("UpdatorId")!);
    }

    [Fact]
    public void UserPagedResponse_SerializesCorrectedAndLegacyPropertyNames()
    {
        var json = JsonSerializer.Serialize(
            new UserPagedResponse { UpdaterId = 42 },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var document = JsonDocument.Parse(json);

        Assert.Equal(42, document.RootElement.GetProperty("updaterId").GetInt32());
        Assert.Equal(42, document.RootElement.GetProperty("updatorId").GetInt32());
    }

    private static void AssertObsolete(MemberInfo member) =>
        Assert.NotNull(member.GetCustomAttribute<ObsoleteAttribute>());
}
