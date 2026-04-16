using Autofac;
using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Application.Demo;
using MS.Microservice.Web.Application.Queries;
using MS.Microservice.Web.Application.Queries.Constract;
using MS.Microservice.Web.Application.Users;
using MS.Microservice.Web.Infrastructure.Applications.Users;
using MS.Microservice.Web.Infrastructure.Dapper;

namespace MS.Microservice.Web.AutofacModules
{
    public class AppServiceModule : Module
    {
        public ConnectionString ConnectionString { get; }
        public AppServiceModule(ConnectionString connectionString)
        {
            ConnectionString = connectionString;
        }
        protected override void Load(ContainerBuilder builder)
        {
            //接口指定服务实现类型
            //builder.RegisterType<OrderService>()
            //    .As<IOrderService>()
            //    .InstancePerDependency();
            builder.RegisterType<CurrentUserResolver>()
                .AsSelf()
                .InstancePerLifetimeScope();

            builder.Register(c => new UserQuery(ConnectionString))
                .As<IUserQuery>()
                .InstancePerLifetimeScope();

            builder.RegisterType<UserCreateAppService>()
                .As<IUserCreateAppService>()
                .InstancePerLifetimeScope();

            builder.RegisterType<UserModifyAppService>()
                .As<IUserModifyAppService>()
                .InstancePerLifetimeScope();

            // -----------------------------------------------------------------------
            // 演示 7.5.4 组合应用程序 — ChangePassword
            // -----------------------------------------------------------------------
            // TrySaveChangePassword 是一个三参数通用函数：
            //   ConnectionString → string(SQL) → ChangePasswordCommand → Task<Either<Error, bool>>
            //
            // 通过两次 Apply 将其特化为一元函数，这正是书中 Sql.TryExecute.Apply(...) 的等价写法：
            //   .Apply(ConnectionString)   固化数据库连接串
            //   .Apply(UpdatePasswordSql)  固化 SQL 模板
            // 最终 save 只需要业务命令这一个参数。
            Func<ChangePasswordCommand, Task<Either<Error, bool>>> saveChangePassword =
                DemoSqlQueries.TrySaveChangePassword
                    .Apply(ConnectionString)
                    .Apply(DemoSqlQueries.UpdatePasswordSql);

            // validate 同样是纯函数，对应书中：var validate = Validation.DateNotPast(...)
            Func<ChangePasswordCommand, Validation<ChangePasswordCommand>> validateChangePassword =
                ChangePasswordValidators.Validate;

            builder.Register(_ => new ChangePasswordAppService(validateChangePassword, saveChangePassword))
                .As<IChangePasswordAppService>()
                .InstancePerLifetimeScope();

            // -----------------------------------------------------------------------
            // 演示 7.6.2 聚合验证结果 — RegisterAccount
            // -----------------------------------------------------------------------
            // save 同样用两次 Apply 特化，与 ChangePassword 模式完全一致。
            Func<RegisterAccountCommand, Task<Either<Error, bool>>> saveRegisterAccount =
                DemoSqlQueries.TrySaveRegisterAccount
                    .Apply(ConnectionString)
                    .Apply(DemoSqlQueries.InsertAccountSql);

            // RegisterAccountAppService 内部使用 HarvestErrors 聚合四个验证器，
            // 组合根无需关心验证器细节，只负责传入 save 函数即可。
            builder.Register(_ => new RegisterAccountAppService(saveRegisterAccount))
                .As<IRegisterAccountAppService>()
                .InstancePerLifetimeScope();
        }
    }
}
