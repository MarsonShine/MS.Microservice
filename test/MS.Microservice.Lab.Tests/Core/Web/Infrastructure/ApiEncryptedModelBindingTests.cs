using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.AspNetCore.Encryption;
using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Lab.Controller;
using MS.Microservice.Lab.Infrastructure.Encryption;
using NSubstitute;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public sealed class ApiEncryptedModelBindingTests
{
    private static readonly Lazy<(string Public, string Private)> Keys = new(() =>
    {
        using var rsa = RSA.Create(2048);
        return (Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));
    });

    [Fact]
    public async Task EnabledBinderDecryptsModernEnvelopeIntoRegisteredModel()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: true);
        var envelope = Encrypt(new EncryptedEchoRequest("中文 hello"));

        using var response = await fixture.Client.PostAsJsonAsync("/api/lab/encryption/echo", envelope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("中文 hello", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DisabledBinderKeepsOrdinaryJsonBinding()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: false);

        using var response = await fixture.Client.PostAsJsonAsync("/api/lab/encryption/echo",
            new EncryptedEchoRequest("plain body"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("plain body", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NoEncryptActionAcceptsPlainJsonWhileEncryptionIsEnabled()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: true);

        using var response = await fixture.Client.PostAsJsonAsync("/api/lab/encryption/plain",
            new EncryptedEchoRequest("plain exception"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("plain exception", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnregisteredModelStillUsesNormalMvcBinding()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: true);

        using var response = await fixture.Client.PostAsJsonAsync("/api/lab/encryption/ordinary",
            new OrdinaryEchoRequest("ordinary"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ordinary", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("tampered")]
    [InlineData("legacy-key")]
    [InlineData("wrong-rsa-key")]
    [InlineData("legacy-body")]
    [InlineData("missing-field")]
    public async Task InvalidOrLegacyEnvelopeIsRejectedBeforeAction(string variant)
    {
        await using var fixture = await Fixture.CreateAsync(enabled: true);
        var envelope = Encrypt(new EncryptedEchoRequest("secret message"));
        object body = variant switch
        {
            "plain" => new EncryptedEchoRequest("secret message"),
            "tampered" => envelope with { Info = Tamper(envelope.Info!) },
            "legacy-key" => envelope with { Key = envelope.Key!["msenc:v1:rsa-oaep-sha256:".Length..] },
            "wrong-rsa-key" => envelope with { Key = EncryptWithAnotherRsaKey() },
            "legacy-body" => envelope with { Info = Convert.ToBase64String("old 3DES"u8) },
            _ => new SafeDataWrapper(null, envelope.Info)
        };

        using var response = await fixture.Client.PostAsJsonAsync("/api/lab/encryption/echo", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("secret message", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void EnabledBinderRequiresAConfiguredPrivateKey()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiEncryptOptions:IsEnabled"] = "true"
        }).Build();
        var services = new ServiceCollection();
        var mvc = services.AddControllers();

        var exception = Assert.Throws<ArgumentException>(() => mvc.AddApiDecryptModelBinding(
            configuration, static models => models.Add(LabApiEncryptionJsonContext.Default.EncryptedEchoRequest)));

        Assert.Contains("ApiEncryptOptions:PrivateKey", exception.Message);
    }

    [Fact]
    public void InvalidEnableSettingFailsDuringRegistration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiEncryptOptions:IsEnabled"] = "sometimes"
        }).Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddControllers()
            .AddApiDecryptModelBinding(configuration,
                static models => models.Add(LabApiEncryptionJsonContext.Default.EncryptedEchoRequest)));

        Assert.Contains("ApiEncryptOptions:IsEnabled", exception.Message);
    }

    [Fact]
    public void LegacySmallRsaKeyFailsDuringRegistration()
    {
        using var rsa = RSA.Create(1024);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiEncryptOptions:IsEnabled"] = "true",
            ["ApiEncryptOptions:PrivateKey"] = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey())
        }).Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddControllers()
            .AddApiDecryptModelBinding(configuration,
                static models => models.Add(LabApiEncryptionJsonContext.Default.EncryptedEchoRequest)));

        Assert.Contains("2048", exception.Message);
    }

    [Fact]
    public void EncryptedModelWithoutJsonMetadataCannotFallBackToPlainBinding()
    {
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(UnregisteredEncryptedRequest));
        var context = Substitute.For<ModelBinderProviderContext>();
        context.Metadata.Returns(metadata);
        var provider = new ApiDecryptModelBinderProvider(new ApiEncryptedModelTypes(), Keys.Value.Private);

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetBinder(context));

        Assert.Contains(nameof(UnregisteredEncryptedRequest), exception.Message);
    }

    private static SafeDataWrapper Encrypt(EncryptedEchoRequest model)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var json = JsonSerializer.Serialize(model, LabApiEncryptionJsonContext.Default.EncryptedEchoRequest);
            return new SafeDataWrapper(
                CryptologyHelper.RsaCrypt.Encrypt(Convert.ToBase64String(key), Keys.Value.Public, Encoding.UTF8),
                CryptologyHelper.AesCrypt.Encrypt(key, json));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string Tamper(string info)
    {
        var start = info.LastIndexOf(':') + 1;
        var bytes = Convert.FromBase64String(info[start..]);
        bytes[^1] ^= 1;
        return info[..start] + Convert.ToBase64String(bytes);
    }

    private static string EncryptWithAnotherRsaKey()
    {
        using var rsa = RSA.Create(2048);
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            return CryptologyHelper.RsaCrypt.Encrypt(Convert.ToBase64String(key),
                Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), Encoding.UTF8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private sealed class Fixture(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public static async Task<Fixture> CreateAsync(bool enabled)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration["ApiEncryptOptions:IsEnabled"] = enabled.ToString();
            if (enabled) builder.Configuration["ApiEncryptOptions:PrivateKey"] = Keys.Value.Private;
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(EncryptionController).Assembly)
                .AddApiDecryptModelBinding(builder.Configuration,
                    static models => models.Add(LabApiEncryptionJsonContext.Default.EncryptedEchoRequest));
            var app = builder.Build();
            app.MapControllers();
            await app.StartAsync();
            return new(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed record UnregisteredEncryptedRequest(string Message) : IApiEncrypt;
}
