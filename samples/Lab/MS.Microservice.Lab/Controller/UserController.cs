using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Consts;
using MS.Microservice.Lab.Application.Commands;
using MS.Microservice.Lab.Application.Models;
using MS.Microservice.Lab.Application.Queries.Constract;
using MS.Microservice.Lab.Application.Users;
using MS.Microservice.Lab.Infrastructure.Http;
using System.Net;
using Wolverine;

namespace MS.Microservice.Lab.Controller
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize(Policy = "Manage")]
    public class UserController(
        IMessageBus messageBus,
        IUserQuery userQuery,
        IUserCreateAppService userCreateAppService,
        IUserModifyAppService userModifyAppService) : ControllerBase
    {
        private readonly IMessageBus _messageBus = messageBus;
        private readonly IUserQuery _userQuery = userQuery;
        private readonly IUserCreateAppService _userCreateAppService = userCreateAppService;
        private readonly IUserModifyAppService _userModifyAppService = userModifyAppService;

        /// <summary>
        /// 创建用户
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>        
        [HttpPost("create")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ResultDto<bool>), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.Conflict)]
        public async Task<IActionResult> CreateUser([FromBody] UserCreatedCommand request)
        {
            var result = await _userCreateAppService.CreateAsync(request, HttpContext.RequestAborted);
            return result.Match<IActionResult>(
                left: error => this.ToProblem(error),
                right: success => Ok(new ResultDto<bool>(success, true, "", 200)));
        }


        /// <summary>
        /// 用户列表
        /// </summary>
        /// <param name="account">账号（精准查找）</param>
        /// <param name="pagedRequest"></param>
        /// <returns></returns>
        [HttpGet("list")]
        [ProducesResponseType(typeof(ResultDto<PagedResultDto<UserPagedResponse>>), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> List([FromQuery]string account, [FromQuery] PagedRequestDto pagedRequest)
        {
            var list = await _userQuery.GetPagedAsync(account, pagedRequest.PageIndex, pagedRequest.PageSize);
            return Ok(new ResultDto<PagedResultDto<UserPagedResponse>>(list));
        }

        /// <summary>
        /// 角色列表
        /// </summary>
        /// <returns></returns>
        [HttpGet("role/list")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ResultDto<List<RoleResponse>>), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> RoleList()
        {
            var list = await _userQuery.GetAllRoleAsync();
            return Ok(new ResultDto<List<RoleResponse>>(list));
        }

        /// <summary>
        /// 修改用户(角色)
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>        
        [HttpPost("modify")]
        [ProducesResponseType(typeof(ResultDto<bool>), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.NotFound)]
        public async Task<IActionResult> Modify([FromBody] UserModifyCommand request)
        {
            (bool success, string? message) = await _messageBus.InvokeAsync<(bool, string?)>(request);
            if (!success)
            {
                var error = string.Equals(message, ExceptionConsts.UserNotExisted, StringComparison.Ordinal)
                    ? Error.NotFound(message!)
                    : Error.Validation(message ?? "用户修改失败。");
                return this.ToProblem(error);
            }

            return Ok(new ResultDto<bool>(true, true, string.Empty, 200));
        }

        /// <summary>
        /// 修改用户(角色) - 函数式组合示例
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("modify-functional")]
        [ProducesResponseType(typeof(ResultDto<bool>), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.NotFound)]
        public async Task<IActionResult> ModifyFunctional([FromBody] UserModifyCommand request)
        {
            var result = await _userModifyAppService.ModifyAsync(request, HttpContext.RequestAborted);
            return result.Match<IActionResult>(
                left: error => this.ToProblem(error),
                right: success => Ok(new ResultDto<bool>(success, true, "", 200)));
        }
    }
}
