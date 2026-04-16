using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Commands;

namespace MS.Microservice.Web.Application.Demo
{
    /// <summary>
    /// 注册账号应用服务接口。
    /// </summary>
    public interface IRegisterAccountAppService
    {
        /// <summary>
        /// 注册账号用例。返回成功/失败的函数式结果：Left 表示校验/持久化失败，Right(true) 表示注册成功。
        /// </summary>
        Task<Either<Error, bool>> RegisterAsync(RegisterAccountCommand command, CancellationToken cancellationToken = default);
    }
}
