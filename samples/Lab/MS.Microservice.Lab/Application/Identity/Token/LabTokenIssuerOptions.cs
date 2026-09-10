using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain.Identity;

namespace MS.Microservice.Lab.Application.Identity.Token;

public sealed class LabTokenIssuerOptions
{
    public const string SectionName = "LabTokenIssuer";
    public string Issuer { get; set; } = "http://localhost:5210";
    public string Audience { get; set; } = "ms-lab";
    public string SigningKey { get; set; } = "";
    public int LifetimeSeconds { get; set; } = 3600;

    public void Validate()
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme is not ("http" or "https")) failures.Add("LabTokenIssuer:Issuer must be an absolute HTTP(S) URI.");
        if (string.IsNullOrWhiteSpace(Audience)) failures.Add("LabTokenIssuer:Audience is required.");
        if (string.IsNullOrWhiteSpace(SigningKey) || SigningKey.Length < 32 || !SigningKey.All(char.IsAscii)) failures.Add("LabTokenIssuer:SigningKey must contain at least 32 ASCII characters.");
        if (LifetimeSeconds is < 1 or > 86400) failures.Add("LabTokenIssuer:LifetimeSeconds must be 1..86400.");
        if (failures.Count > 0) throw new OptionsValidationException(SectionName, typeof(LabTokenIssuerOptions), failures);
    }

    public static ActivationJwtBearerOption ValidationOptions(IConfiguration configuration)
    {
        var issuer = configuration.GetSection(SectionName).Get<LabTokenIssuerOptions>() ?? new();
        issuer.Validate();
        var trusted = configuration.GetSection($"{IdentityOptions.Name}:JwtBearerOption").Get<ActivationJwtBearerOption>() ?? new();
        return new ActivationJwtBearerOption
        {
            Issuers = (trusted.Issuers ?? []).Append(issuer.Issuer).Distinct(StringComparer.Ordinal).ToArray(),
            Audiences = (trusted.Audiences ?? []).Append(issuer.Audience).Distinct(StringComparer.Ordinal).ToArray(),
            SecurityKeys = (trusted.SecurityKeys ?? []).Append(issuer.SigningKey).Distinct(StringComparer.Ordinal).ToArray(),
            Expires = issuer.LifetimeSeconds
        };
    }
}
