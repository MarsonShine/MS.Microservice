using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Application.Models;
using MS.Microservice.Web.Application.Queries.Constract;
using Wolverine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;
using MS.Microservice.Core.Dto;

namespace MS.Microservice.Web.Controller
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize(Policy = "Manage")]
    public class UserController(IMessageBus messageBus, IUserRepository userRepository, IUserQuery userQuery) : ControllerBase
    {
        private readonly IMessageBus _messageBus = messageBus;
        private readonly IUserRepository _userRepository = userRepository;
        private readonly IUserQuery _userQuery = userQuery;

        /// <summary>
        /// 创建用户
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>        
        [HttpPost("create")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ResultDto<bool>), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> CreateUser([FromBody] UserCreatedCommand request)
        {
            (bool success, string? message) = await _messageBus.InvokeAsync<(bool, string?)>(request);
            return Ok(new ResultDto<bool>(success, success, message ?? "", 200));
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
        [ProducesResponseType(typeof(ResultDto<Domain.Identity.ActionResult>), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<IActionResult> Modify([FromBody] UserModifyCommand request)
        {

            (bool success, string? message) = await _messageBus.InvokeAsync<(bool, string?)>(request);
            return Ok(new ResultDto<bool>(success, success, message ?? "", 200));
        }
    }
}
