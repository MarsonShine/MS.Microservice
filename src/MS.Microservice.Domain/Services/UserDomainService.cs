using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;
using MS.Microservice.Domain.Consts;
using MS.Microservice.Domain.Exception;
using MS.Microservice.Domain.Services.Interfaces;

namespace MS.Microservice.Domain.Services
{
    public class UserDomainService : IUserDomainService
    {
        private readonly IUserRepository _userRepository;

        public UserDomainService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<bool> CreateUserAsync(User user, CancellationToken cancellationToken = default)
        {
            var existUser = await _userRepository.FindOptionAsync(p => p.Account == user.Account, cancellationToken);

            return await existUser
                .Where(existingUser => !existingUser.IsTransient())
                .MatchAsync(
                    none: async () =>
                    {
                        user.ChangePassword();
                        _ = await _userRepository.InsertAsync(user, cancellationToken);

                        //string cacheKey2 = CacheConsts.UserAccountKey + userInfo.Account;
                        //_cache.Remove(cacheKey2);
                        return await _userRepository.UnitOfWork.SaveChangesAsync(cancellationToken) > 0;
                    },
                    some: _ => Task.FromException<bool>(new DomainException(ExceptionConsts.UserExisted)));
        }

        public async Task<bool> DeleteUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            var userInfo = await _userRepository.GetAsync(userId, cancellationToken);
            if (userInfo == null || userInfo.IsTransient())
            {
                return true;
            }

            userInfo.Delete();
            await _userRepository.UpdateAsync(userInfo, cancellationToken);
            return await _userRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        }

        public async Task<List<Role>> GetAllRolesAsync(CancellationToken cancellationToken = default)
        {
            var roles = await _userRepository.GetAllRoleAsync(cancellationToken);
            return roles;
        }

        public async Task<User?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            var existUser = await _userRepository.GetAsync(userId, cancellationToken);
            return existUser;
        }

        public async Task<User?> FindAsync(string account, CancellationToken cancellationToken = default)
        {
            return await _userRepository.FindAsync(u => u.Account == account, cancellationToken);
        }

        public async Task<User?> FindFzAccountAsync(string fzAccount, CancellationToken cancellationToken = default)
        {
            return await _userRepository.FindAsync(u => u.FzAccount == fzAccount, cancellationToken);
        }

        public string PasswordSalt() => PasswordSaltHelper.Generate();

        public async Task<bool> UpdateUserAsync(User user, CancellationToken cancellationToken = default)
        {
            var existUser = await _userRepository.FindAsync(p => p.Account == user.Account, cancellationToken);
            if (existUser == null || existUser.IsTransient())
            {
                throw new DomainException(ExceptionConsts.UserNotExisted);
            }

            if (existUser.Telephone != user.Telephone)
            {
                var existPhoneUser = await _userRepository.FindAsync(p => p.Telephone == user.Telephone && p.Id != user.Id, cancellationToken);
                if (existPhoneUser != null && !existPhoneUser.IsTransient())
                {
                    throw new DomainException(ExceptionConsts.UserExisted);
                }
            }

            existUser.Update(user.Name, user.Telephone, user.Email, user.Password, user.Salt);

            existUser.Roles.RemoveAll(r => !user.Roles.Contains(r));
            existUser.AddRoles(user.Roles);

            await _userRepository.UpdateAsync(existUser, cancellationToken);
            return await _userRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        }
    }
}
