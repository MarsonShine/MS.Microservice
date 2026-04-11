using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Domain.Repository.Extensions;
using MS.Microservice.Core.Dto;
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

        public async Task<Result<bool>> CreateUserResultAsync(User user, CancellationToken cancellationToken = default)
        {
            var existUser = await _userRepository.FindOptionAsync(p => p.Account == user.Account, cancellationToken);

            return await existUser
                .Where(existingUser => !existingUser.IsTransient())
                .MatchAsync(
                    none: async () =>
                    {
                        user.ChangePassword();
                        var insertResult = await _userRepository.InsertResultAsync(user, cancellationToken);
                        return await insertResult.BindAsync(async _ =>
                        {
                            var saveResult = await _userRepository.UnitOfWork.SaveChangesResultAsync(cancellationToken);
                            return saveResult
                                .Ensure(changed => changed > 0, () => new DomainException("用户创建失败"))
                                .Map(_ => true);
                        });
                    },
                    some: _ => Task.FromResult(Result<bool>.Fail(new DomainException(ExceptionConsts.UserExisted))));
        }

        public async Task<bool> CreateUserAsync(User user, CancellationToken cancellationToken = default)
        {
            var result = await CreateUserResultAsync(user, cancellationToken);
            return result.Match(
                onSuccess: success => success,
                onFailure: exception => throw exception);
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
