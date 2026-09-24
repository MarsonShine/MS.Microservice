using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MS.Microservice.AspNetCore.Encryption;

public static class MvcOptionsExtensions
{
    public static IMvcBuilder AddApiDecryptModelBinding(this IMvcBuilder mvc, IConfiguration configuration,
        Action<ApiEncryptedModelTypes> configureModels)
    {
        ArgumentNullException.ThrowIfNull(mvc);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configureModels);

        var setting = configuration["ApiEncryptOptions:IsEnabled"];
        if (setting is null) return mvc;
        if (!bool.TryParse(setting, out var enabled))
            throw new ArgumentException("ApiEncryptOptions:IsEnabled must be true or false.");
        if (!enabled) return mvc;

        var privateKey = configuration["ApiEncryptOptions:PrivateKey"];
        if (string.IsNullOrWhiteSpace(privateKey))
            throw new ArgumentException("ApiEncryptOptions:PrivateKey is required when encrypted binding is enabled.");
        ValidatePrivateKey(privateKey);

        var modelTypes = new ApiEncryptedModelTypes();
        configureModels(modelTypes);
        mvc.AddMvcOptions(options => options.ModelBinderProviders.Insert(0,
            new ApiDecryptModelBinderProvider(modelTypes, privateKey)));
        return mvc;
    }

    private static void ValidatePrivateKey(string privateKey)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(privateKey);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("ApiEncryptOptions:PrivateKey must be a valid RSA private key.", exception);
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(key, out var consumed);
            if (consumed != key.Length || rsa.KeySize < 2048)
                throw new CryptographicException("RSA key is incomplete or smaller than 2048 bits.");
        }
        catch (CryptographicException exception)
        {
            throw new ArgumentException("ApiEncryptOptions:PrivateKey must be a valid RSA private key of at least 2048 bits.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
