using MS.Microservice.Core.Extension;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.Identity.Token
{
    public class BearerTokenGenerator : ITokenGenerator
    {
        private readonly IdentityOptions _identityOptions;
        public BearerTokenGenerator(IOptions<IdentityOptions> identityOptionsAccessor)
        {
            if (identityOptionsAccessor==null||identityOptionsAccessor.Value==null)
            {
                throw new ArgumentNullException(nameof(identityOptionsAccessor));
            }
            _identityOptions = identityOptionsAccessor.Value;
        }
        public async Task<string> Generate(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var subject = await UserClaimHelper.GenerateClaimsAsync(user, _identityOptions.JwtBearerOption!.Audiences![1], _identityOptions.JwtBearerOption.Issuers![1]);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = subject,
                Expires = DateTime.Now.AddSeconds(_identityOptions.JwtBearerOption.Expires),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_identityOptions.JwtBearerOption.SecurityKeys![1].ReadAsByte(Encoding.UTF8)),SecurityAlgorithms.HmacSha256Signature)
            };


            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
