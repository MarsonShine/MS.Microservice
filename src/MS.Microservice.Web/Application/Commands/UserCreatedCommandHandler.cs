using MS.Microservice.Web.Application.Users;

namespace MS.Microservice.Web.Application.Commands
{
    /// <summary>
    /// 兼容 Wolverine 消息入口。
    /// 实际用例逻辑下沉到 <see cref="IUserCreateAppService"/>，让 API 与消息入口共享同一条函数式流程。
    /// </summary>
    public class UserCreatedCommandHandler(IUserCreateAppService userCreateAppService)
    {
        private readonly IUserCreateAppService _userCreateAppService = userCreateAppService;

        public async Task<(bool, string?)> Handle(UserCreatedCommand request, CancellationToken cancellationToken)
        {
            var result = await _userCreateAppService.CreateAsync(request, cancellationToken);
            return (result.Success, result.Message);
        }
    }
}
