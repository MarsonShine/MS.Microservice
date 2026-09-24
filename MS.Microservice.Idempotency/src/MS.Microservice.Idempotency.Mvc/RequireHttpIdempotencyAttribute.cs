using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.Idempotency.Mvc;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireHttpIdempotencyAttribute<TFilter>(string operation) : Attribute, IFilterFactory
    where TFilter : class, IAsyncResourceFilter
{
    public string Operation { get; } = operation;
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        => serviceProvider.GetRequiredService<TFilter>();
}
