using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.Services.Interfaces
{
    public interface IUserDomainService
    {
        Task<Either<Error, bool>> CreateUserEitherAsync(User user, CancellationToken cancellationToken = default);
        Task<Result<bool>> CreateUserResultAsync(User user, CancellationToken cancellationToken = default);
        Task<bool> CreateUserAsync(User user, CancellationToken cancellationToken = default);
        Task<bool> DeleteUserAsync(int userId, CancellationToken cancellationToken = default);
        Task<User?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
        Task<bool> UpdateUserAsync(User user, CancellationToken cancellationToken = default);
        Task<List<Role>> GetAllRolesAsync(CancellationToken cancellationToken = default);

        Task<User?> FindAsync(string account, CancellationToken cancellationToken = default);


        Task<User?> FindFzAccountAsync(string fzAccount, CancellationToken cancellationToken = default);

        string PasswordSalt();
        
    }
}
