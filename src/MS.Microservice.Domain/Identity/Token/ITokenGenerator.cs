using MS.Microservice.Domain.Aggregates.IdentityModel;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.Identity.Token
{
    public interface ITokenGenerator
    {
        Task<string> Generate(User user);
    }
}
