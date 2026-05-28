using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Application.Demo;
using System.Net;

namespace MS.Microservice.Web.Controller
{
    /// <summary>
    /// 学习演示控制器。
    /// <list type="bullet">
    ///   <item>
    ///     <term>POST composition/change-password</term>
    ///     <description>演示 7.5.4 组合应用程序：服务接收预先组合好的函数（Apply 模式）。</description>
    ///   </item>
    ///   <item>
    ///     <term>POST aggregation/register</term>
    ///     <description>演示 7.6.2 聚合验证结果：HarvestErrors 将多个验证器合并为一个。</description>
    ///   </item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public class DemoController(
        IChangePasswordAppService changePasswordAppService,
        IRegisterAccountAppService registerAccountAppService) : ControllerBase
    {
        private readonly IChangePasswordAppService _changePasswordAppService = changePasswordAppService;
        private readonly IRegisterAccountAppService _registerAccountAppService = registerAccountAppService;

        /// <summary>
        /// 演示 7.5.4 组合应用程序 – 修改密码。
        /// <para>
        /// 背后的 <see cref="ChangePasswordAppService"/> 只接收两个函数：
        /// <c>validate</c> 和 <c>save</c>。
        /// 这两个函数在 Autofac 组合根（<c>AppServiceModule</c>）中通过 <c>Apply</c> 装配：
        /// </para>
        /// <code>
        /// var save = DemoSqlQueries.TrySaveChangePassword
        ///     .Apply(connString)        // 固化数据库连接串
        ///     .Apply(UpdatePasswordSql); // 固化 SQL 模板
        /// new ChangePasswordAppService(validate, save);
        /// </code>
        /// </summary>
        [HttpPost("composition/change-password")]
        [ProducesResponseType(typeof(ResultDto<bool>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command)
        {
            var result = await _changePasswordAppService.ChangePasswordAsync(command, HttpContext.RequestAborted);
            var response = result.Match(
                left: error => new ResultDto<bool>(false, false, error.ToDisplayMessage(), 200),
                right: success => new ResultDto<bool>(success, true, "", 200));
            return Ok(response);
        }

        /// <summary>
        /// 演示 7.6.2 聚合验证结果 – 注册账号。
        /// <para>
        /// 背后的 <see cref="RegisterAccountAppService"/> 使用 <c>HarvestErrors</c>
        /// 将四个独立验证器聚合为一个总验证器：
        /// </para>
        /// <code>
        /// var validateAll = new[]
        /// {
        ///     ValidateAccount,   // 账号不能为空
        ///     ValidatePassword,  // 密码至少 8 位
        ///     ValidateEmail,     // 邮件格式
        ///     ValidatePhoneNumber // 手机号 11 位
        /// }.HarvestErrors();
        /// </code>
        /// <para>
        /// 传入一个四个字段都非法的请求，响应中会同时包含所有四条错误。
        /// </para>
        /// </summary>
        [HttpPost("aggregation/register")]
        [ProducesResponseType(typeof(ResultDto<bool>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> Register([FromBody] RegisterAccountCommand command)
        {
            var result = await _registerAccountAppService.RegisterAsync(command, HttpContext.RequestAborted);
            var response = result.Match(
                left: error => new ResultDto<bool>(false, false, error.ToDisplayMessage(), 200),
                right: success => new ResultDto<bool>(success, true, "", 200));
            return Ok(response);
        }
    }
}
