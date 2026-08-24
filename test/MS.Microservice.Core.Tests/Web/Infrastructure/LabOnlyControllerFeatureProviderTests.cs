using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using MS.Microservice.Web.Controller;
using MS.Microservice.Web.Infrastructure.Labs;
using System.Reflection;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public class LabOnlyControllerFeatureProviderTests
{
    public static TheoryData<Type> LabControllerTypes => new()
    {
        typeof(DemoController),
        typeof(ImageController),
        typeof(OrdersController),
        typeof(FeatureManagerController)
    };

    [Theory]
    [MemberData(nameof(LabControllerTypes))]
    public void ExperimentalController_IsMarkedLabOnly(Type controllerType)
    {
        Assert.NotNull(controllerType.GetCustomAttribute<LabOnlyAttribute>());
    }

    [Fact]
    public void PopulateFeature_InProduction_RemovesLabControllersAndKeepsFormalController()
    {
        var feature = CreateFeature();
        var provider = new LabOnlyControllerFeatureProvider(labEndpointsEnabled: false);

        provider.PopulateFeature(Array.Empty<ApplicationPart>(), feature);

        Assert.DoesNotContain(feature.Controllers, controller =>
            controller.IsDefined(typeof(LabOnlyAttribute), inherit: false));
        Assert.Contains(typeof(AccountController).GetTypeInfo(), feature.Controllers);
    }

    [Fact]
    public void PopulateFeature_WhenStartedByLabHost_KeepsAllControllers()
    {
        var feature = CreateFeature();
        var provider = new LabOnlyControllerFeatureProvider(labEndpointsEnabled: true);

        provider.PopulateFeature(Array.Empty<ApplicationPart>(), feature);

        Assert.Equal(5, feature.Controllers.Count);
        foreach (var controllerType in LabControllerTypes)
        {
            Assert.Contains(controllerType.GetTypeInfo(), feature.Controllers);
        }
    }

    private static ControllerFeature CreateFeature()
    {
        var feature = new ControllerFeature();
        feature.Controllers.Add(typeof(AccountController).GetTypeInfo());
        foreach (var controllerType in LabControllerTypes)
        {
            feature.Controllers.Add(controllerType.GetTypeInfo());
        }

        return feature;
    }
}
