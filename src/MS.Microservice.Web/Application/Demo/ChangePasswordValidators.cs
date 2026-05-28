using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Commands;

namespace MS.Microservice.Web.Application.Demo
{
    /// <summary>
    /// 演示 7.5.4 中 <c>validate</c> 函数在当前项目的对应实现。
    /// <para>
    /// 书中代码：
    /// <code>
    /// var validate = Validation.DateNotPast(() =&gt; DateTime.UtcNow);
    /// </code>
    /// 这里的每个静态字段就对应一条纯函数验证规则，不依赖任何外部状态（可注入时间源）。
    /// </para>
    /// <para>
    /// 注意：这里使用 fail-fast 模式（遇到第一个错误即停），与 7.6.2 的 HarvestErrors 不同。
    /// 修改密码场景的规则彼此依赖（顺序有意义），因此 fail-fast 更合适。
    /// </para>
    /// </summary>
    internal static class ChangePasswordValidators
    {
        /// <summary>新旧密码不能相同。</summary>
        internal static readonly Func<ChangePasswordCommand, Validation<ChangePasswordCommand>> NotSamePassword =
            cmd => string.Equals(cmd.OldPassword, cmd.NewPassword, StringComparison.Ordinal)
                ? F.Invalid(Error.Validation("新旧密码不能相同", ["NewPassword 与 OldPassword 值相同。"]))
                : (Validation<ChangePasswordCommand>)F.Valid(cmd);

        /// <summary>新密码长度不足 8 位。</summary>
        internal static readonly Func<ChangePasswordCommand, Validation<ChangePasswordCommand>> PasswordMinLength =
            cmd => cmd.NewPassword.Length < 8
                ? F.Invalid(Error.Validation("新密码长度不足", ["NewPassword 至少需要 8 位字符。"]))
                : (Validation<ChangePasswordCommand>)F.Valid(cmd);

        /// <summary>
        /// 组合以上规则（fail-fast：第一个失败即停）。
        /// 此函数对应书中传递给 <c>BookTransferController</c> 构造函数的 <c>validate</c> 参数。
        /// </summary>
        internal static Validation<ChangePasswordCommand> Validate(ChangePasswordCommand cmd)
        {
            var notSame = NotSamePassword(cmd);
            return notSame.IsInvalid ? notSame : PasswordMinLength(cmd);
        }
    }
}
