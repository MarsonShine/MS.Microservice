using MS.Microservice.Core.Extension;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;
using MS.Microservice.Domain.Consts;
using MS.Microservice.Domain.Exception;
using MS.Microservice.Domain.Services.Interfaces;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
            var existUser = await _userRepository.FindAsync(p => p.Account == user.Account, cancellationToken);
            if (existUser != null && !existUser.IsTransient())
            {
                throw new ActivationDomainException(ExceptionConsts.UserExisted);
            }
            user.ChangePassword();
            var userInfo = await _userRepository.InsertAsync(user, cancellationToken);

            //string cacheKey2 = CacheConsts.UserAccountKey + userInfo.Account;
            //_cache.Remove(cacheKey2);
            return await _userRepository.UnitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }

        public async Task<bool> DeleteUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            var userInfo = await _userRepository.GetAsync(userId, cancellationToken);
            if (userInfo.IsTransient())
            {
                return true;
            }

            //string cacheKey1 = CacheConsts.UserIdKey + userInfo.Id;
            //string cacheKey2 = CacheConsts.UserAccountKey + userInfo.Account;
            //_cache.Remove(cacheKey1);
            //_cache.Remove(cacheKey2);

            userInfo.Delete();
            await _userRepository.UpdateAsync(userInfo, cancellationToken);
            return await _userRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        }

        public async Task<List<Role>> GetAllRolesAsync(CancellationToken cancellationToken = default)
        {
            var roles = await _userRepository.GetAllRoleAsync(cancellationToken);
            return roles;
        }

        public async Task<User> GetUserAsync(int userId, CancellationToken cancellationToken = default)
        {

            ////string cacheKey = CacheConsts.UserIdKey + userId;
            //var existUser = await _cache.GetAsync(cacheKey, async () =>
            //{
            //    return
            //}, cancellationToken);
            var existUser = await _userRepository.GetAsync(userId, cancellationToken);
            return existUser;
        }

        public async Task<User> FindAsync(string account, CancellationToken cancellationToken = default)
        {
            return await _userRepository.FindAsync(u => u.Account == account, cancellationToken);
            //string cacheKey = CacheConsts.UserAccountKey + account;
            //var existUser = await _cache.GetAsync(cacheKey, async () =>
            //{
            //    var user = await _userRepository.FindAsync(u => u.Account == account, cancellationToken);
            //    return user.ToCache();
            //}, cancellationToken);

            //return existUser.ToEntity();
        }

        public async Task<User> FindFzAccountAsync(string fzAccount, CancellationToken cancellationToken = default)
        {
            return await _userRepository.FindAsync(u => u.FzAccount == fzAccount, cancellationToken);

        }


        public string PasswordSalt() => PasswordSaltHelper.Generate();

        public async Task<bool> UpdateUserAsync(User user, CancellationToken cancellationToken = default)
        {
            var existUser = await _userRepository.FindAsync(p => p.Account == user.Account, cancellationToken);
            if (existUser.IsTransient())
            {
                ExceptionHelper.ThrowDomainException(ExceptionConsts.UserNotExisted);
            }

            if (existUser.Telephone != user.Telephone)
            {
                var existPhoneUser = await _userRepository.FindAsync(p => p.Telephone == user.Telephone && p.Id != user.Id, cancellationToken);
                if (existPhoneUser != null && !existPhoneUser.IsTransient())
                {
                    ExceptionHelper.ThrowDomainException(ExceptionConsts.UserExisted);
                }
            }

            existUser.Update(user.Name, user.Telephone, user.Email, user.Password, user.Salt);

            existUser.Roles.RemoveAll(r => !user.Roles.Contains(r));
            existUser.AddRoles(user.Roles);

            //string cacheKey1 = CacheConsts.UserIdKey + existUser.Id;
            //string cacheKey2 = CacheConsts.UserAccountKey + existUser.Account;
            //_cache.Remove(cacheKey1);
            //_cache.Remove(cacheKey2);

            await _userRepository.UpdateAsync(existUser, cancellationToken);
            return await _userRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        }
    }
}
