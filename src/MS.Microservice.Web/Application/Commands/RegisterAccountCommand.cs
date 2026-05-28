namespace MS.Microservice.Web.Application.Commands
{
    /// <summary>
    /// 注册账号请求。
    /// 用于演示 7.6.2 聚合验证结果：多个独立验证器通过 HarvestErrors 合并，
    /// 一次调用可收集所有字段的验证错误，而非仅返回第一个错误。
    /// </summary>
    public sealed record RegisterAccountCommand(
        string Account,
        string Password,
        string Email,
        string PhoneNumber);
}
