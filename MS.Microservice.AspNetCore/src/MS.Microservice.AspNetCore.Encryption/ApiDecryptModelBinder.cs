using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MS.Microservice.Core.Security.Cryptology;

namespace MS.Microservice.AspNetCore.Encryption;

public sealed class ApiDecryptModelBinder(JsonTypeInfo modelTypeInfo, string privateKey) : IModelBinder
{
    private const string ModernRsaPrefix = "msenc:v1:rsa-oaep-sha256:";

    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
        var request = bindingContext.HttpContext.Request;
        var plain = bindingContext.HttpContext.GetEndpoint()?.Metadata.GetMetadata<NoEncryptAttribute>() is not null;

        try
        {
            object? model;
            if (plain)
            {
                model = await JsonSerializer.DeserializeAsync(request.Body, modelTypeInfo,
                    bindingContext.HttpContext.RequestAborted);
            }
            else
            {
                var wrapper = await JsonSerializer.DeserializeAsync(request.Body,
                    ApiEncryptedBodyJsonContext.Default.SafeDataWrapper, bindingContext.HttpContext.RequestAborted);
                if (string.IsNullOrWhiteSpace(wrapper?.Key) || string.IsNullOrWhiteSpace(wrapper.Info) ||
                    !wrapper.Key.StartsWith(ModernRsaPrefix, StringComparison.Ordinal))
                    throw new CryptographicException("Unsupported encrypted request format.");

                var keyText = CryptologyHelper.RsaCrypt.Decrypt(wrapper.Key, privateKey, Encoding.UTF8);
                var key = Convert.FromBase64String(keyText);
                try
                {
                    var json = CryptologyHelper.AesCrypt.Decrypt(key, wrapper.Info);
                    model = JsonSerializer.Deserialize(json, modelTypeInfo);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }

            if (model is null) throw new JsonException("Request model is null.");
            bindingContext.Result = ModelBindingResult.Success(model);
        }
        catch (OperationCanceledException) when (bindingContext.HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException or ArgumentException)
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                plain ? "Request body is invalid." : "Encrypted request body is invalid.");
        }
    }
}
