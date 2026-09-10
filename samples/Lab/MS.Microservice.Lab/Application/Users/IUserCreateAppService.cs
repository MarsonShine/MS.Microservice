using MS.Microservice.Core.Functional;
using MS.Microservice.Lab.Application.Commands;

namespace MS.Microservice.Lab.Application.Users
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
        Task<Either<Error, bool>> CreateAsync(UserCreatedCommand request, CancellationToken cancellationToken = default);
    }
}
