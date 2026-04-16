using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Commands;

namespace MS.Microservice.Web.Application.Demo
{
    /// <summary>
    /// 演示 7.6.2 中"独立的小验证器"——每个静态字段只负责一条规则。
    /// <para>
    /// 这些函数将通过 <c>HarvestErrors()</c> 聚合，形成可一次性收集所有错误的总验证器。
    /// 每个验证器的错误描述放在 <see cref="Error.Details"/> 中，以便聚合后拼接展示。
    /// </para>
    /// </summary>
    internal static class RegisterAccountValidators
    {
        /// <summary>账号不能为空。</summary>
        internal static readonly Func<RegisterAccountCommand, Validation<RegisterAccountCommand>> ValidateAccount =
            cmd => string.IsNullOrWhiteSpace(cmd.Account)
                ? F.Invalid(Error.Validation("账号校验失败", ["Account 不能为空白。"]))
                : (Validation<RegisterAccountCommand>)F.Valid(cmd);

        /// <summary>密码至少 8 位。</summary>
        internal static readonly Func<RegisterAccountCommand, Validation<RegisterAccountCommand>> ValidatePassword =
            cmd => cmd.Password.Length < 8
                ? F.Invalid(Error.Validation("密码校验失败", ["Password 至少需要 8 位字符。"]))
                : (Validation<RegisterAccountCommand>)F.Valid(cmd);

        /// <summary>邮件格式必须包含 '@'。</summary>
        internal static readonly Func<RegisterAccountCommand, Validation<RegisterAccountCommand>> ValidateEmail =
            cmd => !cmd.Email.Contains('@')
                ? F.Invalid(Error.Validation("邮件校验失败", ["Email 格式不正确，必须包含 '@'。"]))
                : (Validation<RegisterAccountCommand>)F.Valid(cmd);

        /// <summary>手机号必须是 11 位纯数字。</summary>
        internal static readonly Func<RegisterAccountCommand, Validation<RegisterAccountCommand>> ValidatePhoneNumber =
            cmd => cmd.PhoneNumber.Length != 11 || !cmd.PhoneNumber.All(char.IsDigit)
                ? F.Invalid(Error.Validation("手机号码校验失败", ["PhoneNumber 必须是 11 位数字。"]))
                : (Validation<RegisterAccountCommand>)F.Valid(cmd);
    }
}
