using MS.Microservice.Core.Dto;
using MS.Microservice.Web.Application.Commands;

namespace MS.Microservice.Web.Application.Users
{
    /// <summary>
    /// 用户创建应用服务。
    /// 该层负责把 API 输入组织成可执行的业务用例，再委托给领域层完成核心规则。
    /// </summary>
    public interface IUserCreateAppService
    {
        /// <summary>
        /// 执行创建用户用例。
        /// </summary>
        Task<Result<bool>> CreateAsync(UserCreatedCommand request, CancellationToken cancellationToken = default);
    }
}
