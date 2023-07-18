using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Infrastructure.DbContext;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Action = MS.Microservice.Domain.Aggregates.IdentityModel.Action;

namespace MS.Microservice.Web.Infrastructure
{
    public class ActivationDbContextSeed
    {
        public async Task SeedAsync(ActivationDbContext context, [NotNull] IWebHostEnvironment env, [AllowNull] ILogger<ActivationDbContext> logger)
        {
            if (env.IsDevelopment())
            {
                if (!context.Database.EnsureCreated())
                {
                    await context.Database.MigrateAsync();
                    await InitRoleTableAsync(context);
                    //InitUserTable(context);
                    //InitMerchandiseTable(context);
                }
            }
        } 

        private static void InitDataProductTable(ModelBuilder modelBuilder)
        {
            throw new NotImplementedException();
        }

        private static async Task InitRoleTableAsync(ActivationDbContext context)
        {
            //创建各种角色允许的操作权限
            var roles = new Role[]
            {
                new Role("Administrator", "管理员"), //1
                new Role("SettingManager", "配置管理员"), //2
                new Role("BusinessManager", "商务"), //3
                new Role("ProductManager", "产品"), //4
                new Role("Service", "客服"), //5
                new Role("FinancialManager", "财务"), //6
                new Role("ExternalStaff", "外部人员"), //7
            };

            if (await context.Roles.CountAsync() == 0)
            {
                context.AddRange(roles);
                context.SaveChanges();
            }

            var acts = new Action[]
            {
                new Action("创建批次", "ActivateBatch/Create"), //1
                new Action("启用禁用", "ActivateBatch/Enable"), //2
                new Action("批次码更新","ActivateBatch/Update"), //3
                new Action("批次列表", "ActivateBatch/List"), //4
                new Action("批次详情", "ActivateBatch/Get"), //5
                new Action("批次码追加", "ActivateBatch/Append"), //6
                new Action("外部人员查询激活记录", "ActivateBatch/ExternalList"), //7
                new Action("外部人员查询激活详情", "ActivateBatch/ExternalDetail"), //8

                new Action("商品列表", "Merchandise/GetList"), //9
                new Action("商品明细", "Merchandise/Get"), //10
                new Action("创建商品", "Merchandise/Create"), //11

                new Action("用户授权","Account/Auth"), //12
                new Action("用户列表","User/List"), //13
                new Action("修改用户","User/Modify"), //14
                new Action("创建用户", "User/CreateUser"), //15

                new Action("系统日志","Log/List"), //16

                new Action("激活码记录列表", "ActivateCode/UsedList"), //17
                new Action("激活码设备列表", "ActivateCode/UsedActivateCodeDeviceList"), //18
                new Action("导出excel", "ActivateCode/Export"), //19
                //
                new Action("外部查询出版社权限", "ActivateBatch/ExternalPublisher"), //20
            };


            if (await context.Actions.CountAsync() == 0)
            {
                context.AddRange(acts);
                context.SaveChanges();
            }


            if (await context.RoleActions.CountAsync() == 0)
            {


                int roleindex = 1;
                var AdminActs = new RoleAction[]
                {
                //admin 管理员
  
                new RoleAction(roleindex,1), //"创建批次", "ActivateBatch/Create"),
                new RoleAction(roleindex,2), //"启用禁用", "ActivateBatch/Enable"),
                new RoleAction(roleindex,3), //"批次码更新","ActivateBatch/Update"),
                new RoleAction(roleindex,4), //"批次列表", "ActivateBatch/List"),
                new RoleAction(roleindex,5), //"批次详情", "ActivateBatch/Get"),
                new RoleAction(roleindex,6), //"批次码追加", "ActivateBatch/Append"),
                new RoleAction(roleindex,7), //"外部人员查询激活记录", "ActivateBatch/ExternalList"),
                new RoleAction(roleindex,8), //"外部人员查询激活详情", "ActivateBatch/ExternalDetail"),

                new RoleAction(roleindex,9), //"商品列表", "Merchandise/GetList"),
                new RoleAction(roleindex,10), //"商品明细", "Merchandise/Get"),
                new RoleAction(roleindex,11), //"创建商品", "Merchandise/Create"),

                new RoleAction(roleindex,12), //"用户授权","Account/Auth"),
                new RoleAction(roleindex,13), //"用户列表","User/List"),
                new RoleAction(roleindex,14), //"修改用户","User/Modify"),
                new RoleAction(roleindex,15), //"创建用户", "User/CreateUser"),

                new RoleAction(roleindex,16), //"系统日志","Log/List"),

                new RoleAction(roleindex,17), //"激活码记录列表", "ActivateCode/UsedList"),
                new RoleAction(roleindex,18), //"激活码设备列表", "ActivateCode/UsedActivateCodeDeviceList"),
                new RoleAction(roleindex,19), //"导出excel", "ActivateCode/Export"),
                };
                context.AddRange(AdminActs);

                roleindex += 1;
                var SettingActs = new RoleAction[]
                {
                //Setting 配置管理员
                new RoleAction(roleindex,1), //"创建批次", "ActivateBatch/Create"),
                new RoleAction(roleindex,2), //"启用禁用", "ActivateBatch/Enable"),
                new RoleAction(roleindex,3), //"批次码更新","ActivateBatch/Update"),
                new RoleAction(roleindex,4), //"批次列表", "ActivateBatch/List"),
                new RoleAction(roleindex,5), //"批次详情", "ActivateBatch/Get"),
                new RoleAction(roleindex,6), //"批次码追加", "ActivateBatch/Append"),
                new RoleAction(roleindex,7), //"外部人员查询激活记录", "ActivateBatch/ExternalList"),
                new RoleAction(roleindex,8), //"外部人员查询激活详情", "ActivateBatch/ExternalDetail"),

                new RoleAction(roleindex,9), //"商品列表", "Merchandise/GetList"),
                new RoleAction(roleindex,10), //"商品明细", "Merchandise/Get"),
                new RoleAction(roleindex,11), //"创建商品", "Merchandise/Create"),

                new RoleAction(roleindex,12), //"用户授权","Account/Auth"),
                new RoleAction(roleindex,13), //"用户列表","User/List"),
                new RoleAction(roleindex,14), //"修改用户","User/Modify"),
                new RoleAction(roleindex,15), //"创建用户", "User/CreateUser"),

                new RoleAction(roleindex,16), //"系统日志","Log/List"),

                new RoleAction(roleindex,17), //"激活码记录列表", "ActivateCode/UsedList"),
                new RoleAction(roleindex,18), //"激活码设备列表", "ActivateCode/UsedActivateCodeDeviceList"),
                new RoleAction(roleindex,19), //"导出excel", "ActivateCode/Export"),
                };
                context.AddRange(SettingActs);

                roleindex += 1;
                var BusinessActs = new RoleAction[]
                {
                // 商务

                new RoleAction(roleindex,1), //"创建批次", "ActivateBatch/Create"),
                new RoleAction(roleindex,2), //"启用禁用", "ActivateBatch/Enable"),
                new RoleAction(roleindex,3), //"批次码更新","ActivateBatch/Update"),
                new RoleAction(roleindex,4), //"批次列表", "ActivateBatch/List"),
                new RoleAction(roleindex,5), //"批次详情", "ActivateBatch/Get"),
                new RoleAction(roleindex,6), //"批次码追加", "ActivateBatch/Append"),
                new RoleAction(roleindex,7), //"外部人员查询激活记录", "ActivateBatch/ExternalList"),
                new RoleAction(roleindex,8), //"外部人员查询激活详情", "ActivateBatch/ExternalDetail"),

                new RoleAction(roleindex,9), //"商品列表", "Merchandise/GetList"),
                new RoleAction(roleindex,10), //"商品明细", "Merchandise/Get"),
                //new RoleAction(roleindex,11), //"创建商品", "Merchandise/Create"),

                new RoleAction(roleindex,12), //"用户授权","Account/Auth"),
                //new RoleAction(roleindex,13), //"用户列表","User/List"),
                //new RoleAction(roleindex,14), //"修改用户","User/Modify"),
                //new RoleAction(roleindex,15), //"创建用户", "User/CreateUser"),

                //new RoleAction(roleindex,16), //"系统日志","Log/List"),

                new RoleAction(roleindex,17), //"激活码记录列表", "ActivateCode/UsedList"),
                new RoleAction(roleindex,18), //"激活码设备列表", "ActivateCode/UsedActivateCodeDeviceList"),
                new RoleAction(roleindex,19), //"导出excel", "ActivateCode/Export"),
                };
                context.AddRange(BusinessActs);

                roleindex += 1;
                var ProductActs = new RoleAction[]
                {
                // "ProductManager", "产品"

                //new RoleAction(roleindex,1), //"创建批次", "ActivateBatch/Create"),
                //new RoleAction(roleindex,2), //"启用禁用", "ActivateBatch/Enable"),
                //new RoleAction(roleindex,3), //"批次码更新","ActivateBatch/Update"),
                new RoleAction(roleindex,4), //"批次列表", "ActivateBatch/List"),
                new RoleAction(roleindex,5), //"批次详情", "ActivateBatch/Get"),
                //new RoleAction(roleindex,6), //"批次码追加", "ActivateBatch/Append"),
                new RoleAction(roleindex,7), //"外部人员查询激活记录", "ActivateBatch/ExternalList"),
                new RoleAction(roleindex,8), //"外部人员查询激活详情", "ActivateBatch/ExternalDetail"),

                new RoleAction(roleindex,9), //"商品列表", "Merchandise/GetList"),
                new RoleAction(roleindex,10), //"商品明细", "Merchandise/Get"),
                //new RoleAction(roleindex,11), //"创建商品", "Merchandise/Create"),

                new RoleAction(roleindex,12), //"用户授权","Account/Auth"),
                //new RoleAction(roleindex,13), //"用户列表","User/List"),
                //new RoleAction(roleindex,14), //"修改用户","User/Modify"),
                //new RoleAction(roleindex,15), //"创建用户", "User/CreateUser"),

                //new RoleAction(roleindex,16), //"系统日志","Log/List"),

                new RoleAction(roleindex,17), //"激活码记录列表", "ActivateCode/UsedList"),
                new RoleAction(roleindex,18), //"激活码设备列表", "ActivateCode/UsedActivateCodeDeviceList"),
                
                new RoleAction(roleindex,19), //"导出excel", "ActivateCode/Export"),
                };
                context.AddRange(ProductActs);

                roleindex += 1;
                var ServiceActs = new RoleAction[]
                {
                // "Service", "客服"

                //new RoleAction(roleindex,1), //"创建批次", "ActivateBatch/Create"),
                //new RoleAction(roleindex,2), //"启用禁用", "ActivateBatch/Enable"),
                //new RoleAction(roleindex,3), //"批次码更新","ActivateBatch/Update"),
                new RoleAction(roleindex,4), //"批次列表", "ActivateBatch/List"),
                new RoleAction(roleindex,5), //"批次详情", "ActivateBatch/Get"),
                //new RoleAction(roleindex,6), //"批次码追加", "ActivateBatch/Append"),
                new RoleAction(roleindex,7), //"外部人员查询激活记录", "ActivateBatch/ExternalList"),
                new RoleAction(roleindex,8), //"外部人员查询激活详情", "ActivateBatch/ExternalDetail"),

                //new RoleAction(roleindex,9), //"商品列表", "Merchandise/GetList"),
                //new RoleAction(roleindex,10), //"商品明细", "Merchandise/Get"),
                //new RoleAction(roleindex,11), //"创建商品", "Merchandise/Create"),

                new RoleAction(roleindex,12), //"用户授权","Account/Auth"),
                //new RoleAction(roleindex,13), //"用户列表","User/List"),
                //new RoleAction(roleindex,14), //"修改用户","User/Modify"),
                //new RoleAction(roleindex,15), //"创建用户", "User/CreateUser"),

                //new RoleAction(roleindex,16), //"系统日志","Log/List"),

                new RoleAction(roleindex,17), //"激活码记录列表", "ActivateCode/UsedList"),
                new RoleAction(roleindex,18), //"激活码设备列表", "ActivateCode/UsedActivateCodeDeviceList"),
                
                //new RoleAction(roleindex,19), //"导出excel", "ActivateCode/Export"),
                };
                context.AddRange(ServiceActs);

                roleindex += 1;
                var FinancialActs = new RoleAction[]
                {
                // FinancialManager", "财务"

                //new RoleAction(roleindex,1), //"创建批次", "ActivateBatch/Create"),
                //new RoleAction(roleindex,2), //"启用禁用", "ActivateBatch/Enable"),
                //new RoleAction(roleindex,3), //"批次码更新","ActivateBatch/Update"),
                new RoleAction(roleindex,4), //"批次列表", "ActivateBatch/List"),
                new RoleAction(roleindex,5), //"批次详情", "ActivateBatch/Get"),
                //new RoleAction(roleindex,6), //"批次码追加", "ActivateBatch/Append"),
                new RoleAction(roleindex,7), //"外部人员查询激活记录", "ActivateBatch/ExternalList"),
                new RoleAction(roleindex,8), //"外部人员查询激活详情", "ActivateBatch/ExternalDetail"),

                new RoleAction(roleindex,9), //"商品列表", "Merchandise/GetList"),
                new RoleAction(roleindex,10), //"商品明细", "Merchandise/Get"),
                //new RoleAction(roleindex,11), //"创建商品", "Merchandise/Create"),

                new RoleAction(roleindex,12), //"用户授权","Account/Auth"),
                //new RoleAction(roleindex,13), //"用户列表","User/List"),
                //new RoleAction(roleindex,14), //"修改用户","User/Modify"),
                //new RoleAction(roleindex,15), //"创建用户", "User/CreateUser"),

                //new RoleAction(roleindex,16), //"系统日志","Log/List"),

                new RoleAction(roleindex,17), //"激活码记录列表", "ActivateCode/UsedList"),
                new RoleAction(roleindex,18), //"激活码设备列表", "ActivateCode/UsedActivateCodeDeviceList"),
                
                //new RoleAction(roleindex,19), //"导出excel", "ActivateCode/Export"),
                };
                context.AddRange(FinancialActs);

                roleindex += 1;
                var ExternalActs = new RoleAction[]
               {
                // "ExternalStaff", "外部人员"

                //new RoleAction(roleindex,1), //"创建批次", "ActivateBatch/Create"),
                //new RoleAction(roleindex,2), //"启用禁用", "ActivateBatch/Enable"),
                //new RoleAction(roleindex,3), //"批次码更新","ActivateBatch/Update"),
                //new RoleAction(roleindex,4), //"批次列表", "ActivateBatch/List"),
                //new RoleAction(roleindex,5), //"批次详情", "ActivateBatch/Get"),
                //new RoleAction(roleindex,6), //"批次码追加", "ActivateBatch/Append"),
                new RoleAction(roleindex,7), //"外部人员查询激活记录", "ActivateBatch/ExternalList"),
                new RoleAction(roleindex,8), //"外部人员查询激活详情", "ActivateBatch/ExternalDetail"),

                //new RoleAction(roleindex,9), //"商品列表", "Merchandise/GetList"),
                //new RoleAction(roleindex,10), //"商品明细", "Merchandise/Get"),
                //new RoleAction(roleindex,11), //"创建商品", "Merchandise/Create"),

                new RoleAction(roleindex,12), //"用户授权","Account/Auth"),
                
                //new RoleAction(roleindex,13), //"用户列表","User/List"),
                //new RoleAction(roleindex,14), //"修改用户","User/Modify"),
                //new RoleAction(roleindex,15), //"创建用户", "User/CreateUser"),

                //new RoleAction(roleindex,16), //"系统日志","Log/List"),

                //new RoleAction(roleindex,17), //"激活码记录列表", "ActivateCode/UsedList"),
                //new RoleAction(roleindex,18), //"激活码设备列表", "ActivateCode/UsedActivateCodeDeviceList"),
                //new RoleAction(roleindex,19), //"导出excel", "ActivateCode/Export"),
               };
                context.AddRange(ExternalActs);


                context.SaveChanges();
            }

        }

        private static void InitUserTable(ActivationDbContext context)
        {
            var user = new User("FZ202110220001", "Fz123456", "FZHS", false, "18975152023", 0, 0, "shuai.mao@kingsunsoft.com", "毛帅", "FZXXXX000001", "雪花ID");
            context.Add(
                user
                );
            context.SaveChanges();

            context.Add(new UserRole(1, 1));
            context.SaveChanges();
        }
    }
}
