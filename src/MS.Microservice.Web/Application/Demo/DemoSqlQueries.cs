using Dapper;
using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Infrastructure.Dapper;

namespace MS.Microservice.Web.Application.Demo
{
    /// <summary>
    /// 演示 7.5.4 中"通用 Sql.TryExecute"概念在当前项目的对应实现。
    /// <para>
    /// 书中代码：
    /// <code>
    /// var save = Sql.TryExecute
    ///     .Apply(connString)
    ///     .Apply(Sql.Queries.InsertTransferOn);
    /// </code>
    /// 这里的 <see cref="TrySaveChangePassword"/> 和 <see cref="TrySaveRegisterAccount"/>
    /// 就对应书中的 <c>Sql.TryExecute</c>：三参数通用函数。
    /// 通过两次 <c>Apply</c> 即可将其特化为只需传入业务命令的一元函数。
    /// </para>
    /// </summary>
    internal static class DemoSqlQueries
    {
        /// <summary>修改密码 SQL（Dapper 参数化查询）。</summary>
        internal const string UpdatePasswordSql =
            "UPDATE users SET password_hash = @NewPasswordHash " +
            "WHERE account = @Account AND password_hash = @OldPasswordHash";

        /// <summary>注册账号 SQL。</summary>
        internal const string InsertAccountSql =
            "INSERT INTO users (account, password_hash, email, phone_number, created_at) " +
            "VALUES (@Account, @PasswordHash, @Email, @PhoneNumber, @CreatedAt)";

        /// <summary>
        /// 修改密码的通用三参数执行函数。
        /// <para>
        /// 类型签名：<c>ConnectionString → string → ChangePasswordCommand → Task&lt;Either&lt;Error, bool&gt;&gt;</c>
        /// </para>
        /// <para>
        /// 在组合根（<c>AppServiceModule</c>）中使用两次 <c>Apply</c> 将其特化：
        /// <code>
        /// // 固化连接串 → SQL → Command
        /// Func&lt;ChangePasswordCommand, Task&lt;Either&lt;Error, bool&gt;&gt;&gt; save =
        ///     TrySaveChangePassword
        ///         .Apply(connString)       // 固化数据库连接
        ///         .Apply(UpdatePasswordSql); // 固化 SQL 模板
        /// </code>
        /// </para>
        /// </summary>
        internal static readonly Func<ConnectionString, string, ChangePasswordCommand, Task<Either<Error, bool>>>
            TrySaveChangePassword =
                (connString, sql, cmd) => EitherExtensions.TryAsync(async () =>
                {
                    await using var connection = connString.CreateConnection();
                    await connection.OpenAsync();
                    var affected = await connection.ExecuteAsync(sql, new
                    {
                        cmd.Account,
                        OldPasswordHash = cmd.OldPassword,
                        NewPasswordHash = cmd.NewPassword
                    });
                    return affected > 0;
                }, code: "demo.change-password");

        /// <summary>
        /// 注册账号的通用三参数执行函数。
        /// <para>
        /// 类型签名：<c>ConnectionString → string → RegisterAccountCommand → Task&lt;Either&lt;Error, bool&gt;&gt;</c>
        /// </para>
        /// </summary>
        internal static readonly Func<ConnectionString, string, RegisterAccountCommand, Task<Either<Error, bool>>>
            TrySaveRegisterAccount =
                (connString, sql, cmd) => EitherExtensions.TryAsync(async () =>
                {
                    await using var connection = connString.CreateConnection();
                    await connection.OpenAsync();
                    var affected = await connection.ExecuteAsync(sql, new
                    {
                        cmd.Account,
                        PasswordHash = cmd.Password,
                        cmd.Email,
                        cmd.PhoneNumber,
                        CreatedAt = DateTime.UtcNow
                    });
                    return affected > 0;
                }, code: "demo.register-account");
    }
}
