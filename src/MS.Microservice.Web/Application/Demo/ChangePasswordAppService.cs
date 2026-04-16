using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Commands;

namespace MS.Microservice.Web.Application.Demo
{
    /// <summary>
    /// 演示 7.5.4 组合应用程序。
    /// <para>
    /// 书中的 <c>BookTransferController</c> 直接接收 <c>validate</c> 和 <c>save</c> 函数，
    /// 而不是具体的服务实例。本服务遵循同样的思想：
    /// <list type="bullet">
    ///   <item><c>validate</c> 和 <c>save</c> 在组合根（<see cref="MS.Microservice.Web.AutofacModules.AppServiceModule"/>）
    ///   通过 <c>Apply</c> 预先装配好，再注入到此服务。</item>
    ///   <item>本服务完全不知道数据库连接串是什么、SQL 是什么，
    ///   它只关心"如何把验证结果和保存动作接起来"。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 对应书中代码清单 7.15：
    /// <code>
    /// return new BookTransferController(validate, save);
    /// </code>
    /// 这里改为：
    /// <code>
    /// new ChangePasswordAppService(validate, save)
    /// </code>
    /// </para>
    /// </summary>
    public sealed class ChangePasswordAppService : IChangePasswordAppService
    {
        // validate：纯函数，不做 IO，只检查业务规则
        private readonly Func<ChangePasswordCommand, Validation<ChangePasswordCommand>> _validate;

        // save：已由组合根通过 Apply 固化了连接串和 SQL，只剩业务命令这一个参数
        private readonly Func<ChangePasswordCommand, Task<Either<Error, bool>>> _save;

        /// <summary>
        /// 构造函数接收函数而非服务实例，体现组合根负责组装的原则。
        /// </summary>
        public ChangePasswordAppService(
            Func<ChangePasswordCommand, Validation<ChangePasswordCommand>> validate,
            Func<ChangePasswordCommand, Task<Either<Error, bool>>> save)
        {
            _validate = validate;
            _save = save;
        }

        /// <inheritdoc/>
        public Task<Either<Error, bool>> ChangePasswordAsync(
            ChangePasswordCommand command,
            CancellationToken cancellationToken = default)
        {
            // 第一步：纯函数验证（不做 IO）
            var validation = _validate(command);

            // 第二步：验证通过才调用 save，否则直接返回 Left(error)
            // 这与书中 BookTransferController 的做法完全一致
            return validation.Match<Task<Either<Error, bool>>>(
                invalid: error => Task.FromResult((Either<Error, bool>)F.Left(error)),
                valid: validCmd => _save(validCmd));
        }
    }
}
