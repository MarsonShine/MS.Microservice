using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MS.Microservice.AspNetCore.Encryption;

public sealed class ApiDecryptModelBinderProvider(ApiEncryptedModelTypes modelTypes, string privateKey)
    : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var modelType = context.Metadata.ModelType;
        if (modelTypes.TryGet(modelType, out var typeInfo))
            return new ApiDecryptModelBinder(typeInfo, privateKey);
        if (typeof(IApiEncrypt).IsAssignableFrom(modelType))
            throw new InvalidOperationException($"Register JsonTypeInfo for encrypted model {modelType.FullName}.");
        return null;
    }
}
