using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MS.Microservice.Core.Identity;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Identity.Token;

namespace MS.Microservice.Lab.Application.Identity.Token;

public sealed class BearerTokenGenerator(IOptions<LabTokenIssuerOptions> options, TimeProvider clock) : ITokenGenerator
{
    public Task<string> Generate(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var issuer = options.Value;
        issuer.Validate();
        var now = clock.GetUtcNow().UtcDateTime;
        var id = user.Id.ToString(CultureInfo.InvariantCulture);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer.Issuer, Audience = issuer.Audience, IssuedAt = now, NotBefore = now,
            Expires = now.AddSeconds(issuer.LifetimeSeconds),
            Subject = new ClaimsIdentity([
                new("sub", id), new(JwtClaimTypes.Id, id),
                new(JwtClaimTypes.NickName, user.Name ?? user.Account ?? id),
                new(JwtClaimTypes.PhoneNumber, user.Telephone ?? ""), new(JwtClaimTypes.Email, user.Email ?? ""),
                new(JwtClaimTypes.Role, string.Join(';', user.Roles.Select(role => role.Id))),
                new(JwtClaimTypes.Issuer, issuer.Issuer), new(JwtClaimTypes.Audience, issuer.Audience)
            ]),
            SigningCredentials = new(new SymmetricSecurityKey(Encoding.ASCII.GetBytes(issuer.SigningKey)), SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return Task.FromResult(handler.WriteToken(handler.CreateToken(descriptor)));
    }
}
