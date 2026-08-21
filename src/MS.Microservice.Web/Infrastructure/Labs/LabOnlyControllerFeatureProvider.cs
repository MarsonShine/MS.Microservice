using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace MS.Microservice.Web.Infrastructure.Labs;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class LabOnlyAttribute : Attribute;

public sealed class LabOnlyControllerFeatureProvider(string environmentName)
    : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly bool _labEndpointsEnabled =
        string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Lab", StringComparison.OrdinalIgnoreCase);

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(feature);

        if (_labEndpointsEnabled)
        {
            return;
        }

        for (var index = feature.Controllers.Count - 1; index >= 0; index--)
        {
            if (feature.Controllers[index].IsDefined(typeof(LabOnlyAttribute), inherit: false))
            {
                feature.Controllers.RemoveAt(index);
            }
        }
    }
}
