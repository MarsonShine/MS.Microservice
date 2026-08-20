using MS.Microservice.Web.Application.Validations;
using MS.Microservice.Web.Infrastructure.Applications.Users;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Domain.Consts;
using MS.Microservice.Core.Extension;
using MS.Microservice.Infrastructure.Caching.Consts;
using MS.Microservice.Web.Application.Identity;
using Wolverine;

namespace MS.Microservice.Web.Application.Commands
{
    public class UserModifyCommandHandler
    {
        private readonly IUserDomainService _userDomainService;
        private readonly CurrentUser _currentUser;
        private readonly IDistributedCache _cache;
        private readonly IUserPasswordService _userPasswordService;
        public UserModifyCommandHandler(
            IUserDomainService userDomainService,
            IDistributedCache cache,
            CurrentUserResolver currentUserResolver,
            IUserPasswordService userPasswordService)
        {
            _userDomainService = userDomainService;
            _currentUser = currentUserResolver.CurrentUser() ?? throw new ArgumentException(nameof(CurrentUserResolver));
            _cache = cache;
            _userPasswordService = userPasswordService;
        }
        public async Task<(bool, string?)> Handle(UserModifyCommand request, CancellationToken cancellationToken)
        {
            var validator = new UserModifyCommandValidator();
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
            var exitUser = await _userDomainService.FindAsync(request.Account, cancellationToken);
            if (exitUser == null)
            {
                //考虑自动创建账号
                //如果创建参数要全传
                return (false, ExceptionConsts.UserNotExisted);
            }

            var user = new Domain.Aggregates.IdentityModel.User(request.Account, string.Empty, string.Empty, false, request.Telephone, _currentUser.Id, _currentUser.Id, request.Email, request.UserName, "", "");
            if (request.Passowrd.IsNotNullOrEmpty())
            {
                user.SetPasswordHash(_userPasswordService.HashPassword(user, request.Passowrd));
            }
            if (request.Roles?.Count > 0)
            {
                user.Roles.AddIfNotContains(
                   request.Roles.Select(r => new Domain.Aggregates.IdentityModel.Role(r.Id, r.Name!, ""))
               );
            }


            await _userDomainService.UpdateUserAsync(user, cancellationToken);

            //清理缓存
            await _cache.RemoveAsync(CacheConsts.UserAccountKey + request.Account, cancellationToken);

            if (exitUser.FzAccount.IsNotNullOrEmpty())
                await _cache.RemoveAsync(CacheConsts.UserFzAccountKey + exitUser.FzAccount, cancellationToken);
            await _cache.RemoveAsync(CacheConsts.UserIdKey + exitUser.Id, cancellationToken);

            return (true, null);
        }
    }
}
