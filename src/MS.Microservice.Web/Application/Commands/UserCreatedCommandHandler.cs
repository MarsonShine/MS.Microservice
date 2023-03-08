using MS.Microservice.Web.Application.Validations;
using MS.Microservice.Web.Infrastructure.Applications.Users;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Core.Extension;

namespace MS.Microservice.Web.Application.Commands
{
    public class UserCreatedCommandHandler : IRequestHandler<UserCreatedCommand, (bool, string)>
    {
        private readonly IUserDomainService _userDomainService;
        private readonly CurrentUser _currentUser;
        private readonly CurrentUserResolver _currentUserResolver;
        private readonly IDistributedCache _cache;
        public UserCreatedCommandHandler(
            IUserDomainService userDomainService,
            IDistributedCache cache,
            CurrentUserResolver currentUserResolver)
        {
            _userDomainService = userDomainService;
            _currentUser = currentUserResolver.CurrentUser();
            _currentUserResolver = currentUserResolver;
            _cache = cache;
        }
        public async Task<(bool, string)> Handle(UserCreatedCommand request, CancellationToken cancellationToken)
        {
            var validator = new UserCreatedCommandValidator();
            var result = await validator.ValidateAsync(request, cancellationToken);
            if (!result.IsValid)
            {
                return (false, result.ToString());
            }
            if (request.Roles.Count > 0)
            {
                var roles = await _userDomainService.GetAllRolesAsync(cancellationToken);
                var roleIds = roles.Select(r => r.Id).ToArray();
                foreach (var r in request.Roles)
                {
                    if (roleIds.Contains(r.Id) == false)
                    {
                        return (false, "错误的角色参数");
                    }
                }
            }

            var tmpCurrentUser = await _currentUserResolver.CurrentUserAsync();
            // 这里调用领域服务
            string salt = _userDomainService.PasswordSalt();
            var user = new Domain.Aggregates.IdentityModel.User(request.Account, request.Passowrd, salt, false, request.Telephone, tmpCurrentUser.Id, tmpCurrentUser.Id, request.Email, request.UserName, request.Account, "");
            if (request.Roles?.Count > 0)
            {
                user.Roles.AddIfNotContains(
                   request.Roles?.Select(r => new Domain.Aggregates.IdentityModel.Role(r.Id, r.Name, ""))
               );
            }

            await _userDomainService.CreateUserAsync(user, cancellationToken);

            //清理缓存
            //await _cache.RemoveAsync(CacheConsts.UserAccountKey + user.Account);

            return (true, null);
        }
    }
}
