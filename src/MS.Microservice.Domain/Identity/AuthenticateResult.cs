using MS.Microservice.Domain.Aggregates.IdentityModel;
using System.Linq;

namespace MS.Microservice.Domain.Identity
{
    public class AuthenticateResult
    {
        public int Id { get; }
        public string? Name { get; }
        public string[]? Roles { get;}
        public string? Token { get; }
        public string? Email { get;}
        public AuthenticateResult(User user, string token)
        {
            Token = token;
            Id = user.Id;
            Name = user.Name;
            Email = user.Email;
            Roles = user.Roles
                .Select(r => r.Name!)
                .ToArray();
        }
    }
}
