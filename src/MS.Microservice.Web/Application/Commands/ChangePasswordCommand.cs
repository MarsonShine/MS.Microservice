namespace MS.Microservice.Web.Application.Commands
{
    /// <summary>
    /// 修改密码请求。
    /// 用于演示 7.5.4 组合应用程序：控制器/服务接收的是已预先组合好的函数，
    /// 而非具体的服务实例。
    /// </summary>
    public sealed record ChangePasswordCommand(
        string Account,
        string OldPassword,
        string NewPassword);
}
