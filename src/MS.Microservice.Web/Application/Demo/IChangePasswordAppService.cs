using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Commands;

namespace MS.Microservice.Web.Application.Demo
{
    /// <summary>
    /// 修改密码应用服务接口。
    /// </summary>
    public interface IChangePasswordAppService
    {
        /// <summary>
        /// 修改密码用例。返回成功/失败的函数式结果：Left 表示验证/持久化失败，Right(true) 表示修改成功。
        /// </summary>
        Task<Either<Error, bool>> ChangePasswordAsync(ChangePasswordCommand command, CancellationToken cancellationToken = default);
    }
}
