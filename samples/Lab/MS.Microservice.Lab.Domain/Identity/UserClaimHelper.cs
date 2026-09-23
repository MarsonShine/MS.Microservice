using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Identity;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.Identity
{
    public class UserClaimHelper
    {
        public static async Task<ClaimsIdentity> GenerateClaimsAsync(User user, string audience, string issuer)
        {
            string userId = user.Id.ToString();
            string userName = user.Name!;
            var id = new ClaimsIdentity(IdentityConstants.ApplicationScheme, JwtClaimTypes.NickName, JwtClaimTypes.Role);
            id.AddClaim(new Claim(JwtClaimTypes.Id, userId));
            id.AddClaim(new Claim(JwtClaimTypes.NickName, userName));
            id.AddClaim(new Claim(JwtClaimTypes.PhoneNumber, user.Telephone!));
            id.AddClaim(new Claim(JwtClaimTypes.Email, user.Email!));

            var roles = user.Roles
                .Select(p => p.Id)
                .ToArray();
            id.AddClaim(new Claim(JwtClaimTypes.Role, roles.JoinAsString(";")));
            id.AddClaim(new Claim(JwtClaimTypes.Audience, audience));
            id.AddClaim(new Claim(JwtClaimTypes.Issuer, issuer));

            return await Task.FromResult(id);
        }

    }
}
