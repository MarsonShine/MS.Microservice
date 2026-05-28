using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Commands;

namespace MS.Microservice.Web.Application.Demo
{
    /// <summary>
    /// 演示 7.6.2 聚合验证结果。
    /// <para>
    /// 多个独立的验证器（每个只负责一条规则）通过 <c>HarvestErrors()</c> 聚合为单一验证器：
    /// <list type="bullet">
    ///   <item>所有规则都会被执行（不短路）。</item>
    ///   <item>如果多个规则失败，所有错误描述都会被收集到
    ///   <see cref="Error.Details"/> 列表中一并返回。</item>
    ///   <item>前端只需一次调用即可得到所有字段的完整校验反馈。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 对应书中 7.6.2 的 HarvestErrors 应用：
    /// <code>
    /// var validateAll = new[]
    /// {
    ///     ValidateAccount,
    ///     ValidatePassword,
    ///     ValidateEmail,
    ///     ValidatePhoneNumber
    /// }.HarvestErrors();
    /// </code>
    /// </para>
    /// </summary>
    public sealed class RegisterAccountAppService : IRegisterAccountAppService
    {
        // 聚合后的总验证器：一次跑完所有规则，收集全部错误
        private static readonly Func<RegisterAccountCommand, Validation<RegisterAccountCommand>> _validate =
            new Func<RegisterAccountCommand, Validation<RegisterAccountCommand>>[]
            {
                RegisterAccountValidators.ValidateAccount,
                RegisterAccountValidators.ValidatePassword,
                RegisterAccountValidators.ValidateEmail,
                RegisterAccountValidators.ValidatePhoneNumber
            }.HarvestErrors();

        // save 同样由组合根通过 Apply 预先装配好
        private readonly Func<RegisterAccountCommand, Task<Either<Error, bool>>> _save;

        /// <summary>
        /// 构造函数只接收 <c>save</c> 函数；<c>validate</c> 在类型加载时一次性聚合完毕。
        /// </summary>
        public RegisterAccountAppService(Func<RegisterAccountCommand, Task<Either<Error, bool>>> save)
        {
            _save = save;
        }

        /// <inheritdoc/>
        public Task<Either<Error, bool>> RegisterAsync(
            RegisterAccountCommand command,
            CancellationToken cancellationToken = default)
        {
            // HarvestErrors 聚合验证：所有规则都会跑，错误全部收集
            var validation = _validate(command);

            return validation.Match<Task<Either<Error, bool>>>(
                invalid: errors => Task.FromResult((Either<Error, bool>)F.Left(
                    Error.Validation("校验失败", errors.SelectMany(e => e.DetailsOrEmpty).ToList()))),
                valid: validCmd => _save(validCmd));
        }
    }
}
