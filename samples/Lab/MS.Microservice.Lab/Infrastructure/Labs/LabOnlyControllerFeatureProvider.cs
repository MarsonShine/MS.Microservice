using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using System.Reflection;

namespace MS.Microservice.Lab.Infrastructure.Labs;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class LabOnlyAttribute : Attribute;

public sealed class LabOnlyControllerFeatureProvider(bool labEndpointsEnabled)
    : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(feature);

        if (labEndpointsEnabled)
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
