using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using IdentityModel;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.Identity
{
    public class UserClaimHelper
    {
        public static async Task<ClaimsIdentity> GenerateClaimsAsync(User user, string audience, string issuer)
        {
            var userId = user.Id.ToString();
            var userName = user.Name;
            var id = new ClaimsIdentity(IdentityConstants.ApplicationScheme, JwtClaimTypes.NickName, JwtClaimTypes.Role);
            id.AddClaim(new Claim(JwtClaimTypes.Id, userId));
            id.AddClaim(new Claim(JwtClaimTypes.NickName, userName));
            id.AddClaim(new Claim(JwtClaimTypes.PhoneNumber, user.Telephone));
            id.AddClaim(new Claim(JwtClaimTypes.Email, user.Email));

            var roles = user.Roles
                .Select(p => p.Id)
                .ToArray();
            id.AddClaim(new Claim(JwtClaimTypes.Role, roles.JoinAsString(";")));
            id.AddClaim(new Claim(JwtClaimTypes.Audience, audience));
            id.AddClaim(new Claim(JwtClaimTypes.Issuer, issuer));

            return await Task.FromResult(id);
        }

        public static User JWT2User(ClaimsIdentity id)
        {
            var Name = id.FindFirst(c => c.Type == JwtClaimTypes.NickName);
            var PhoneNumber = id.FindFirst(c => c.Type == JwtClaimTypes.PhoneNumber);
            var Id = id.FindFirst(c => c.Type == JwtClaimTypes.Id);
            var mail = "mail@kingsunsoft.com";
            var salt = PasswordSaltHelper.Generate();
            var pwd = CryptologyHelper.HmacSha256("Fz123456" + salt);
            var u = new User(PhoneNumber.Value, pwd, salt, false, PhoneNumber.Value, 0, 0, mail, Name.Value, PhoneNumber.Value, Id.Value);

            return u;
        }
    }
}
