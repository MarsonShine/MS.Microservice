using MS.Microservice.Web.Application.Models.AccountRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using MS.Microservice.Domain.Identity;
using MS.Microservice.Domain.Identity.Token;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Core.Dto;
using MS.Microservice.Infrastructure.Attributes;
using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Domain.Consts;

namespace MS.Microservice.Web.Controller
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly SignInManager _signInManager;
        private readonly ITokenGenerator _tokenGenerator;
        private readonly IUserDomainService _userDomainService;
        private readonly IDistributedCache _cache;
        public AccountController(SignInManager signInManager, ITokenGenerator tokenGenerator, IUserDomainService userDomainService,
            IDistributedCache cache)
        {
            _signInManager = signInManager;
            _tokenGenerator = tokenGenerator;
            _userDomainService = userDomainService;

            _cache = cache;
        }

        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("login")]
        [ProducesResponseType(typeof(ResultDto<AuthenticateResult>), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [NoEncrypt]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request.Account == null || request.Password.IsNullOrEmpty())
            {
                return BadRequest();
            }

            byte[]? base64Buffer = null;
            try
            {
                base64Buffer = Convert.FromBase64String(request.Password);
            }
            catch (Exception)
            {
                return BadRequest();
            }
            var passworld = Encoding.UTF8.GetString(base64Buffer);

            //查询用户
            var user = await _userDomainService.FindAsync(request.Account);


            if (user == null || user.IsTransient() || CryptologyHelper.HmacSha256(passworld + user.Salt) != user.Password)
            {
                return Ok(new ResultDto(false, ExceptionConsts.AccountOrPasswordError, 200));
            }
            //var user = new User(request.Account, request.Password, "", false, "18975152023", 1, 1, "marsonshine@163.com", "marsonshine", "", "");
            //user.Id = 1;
            //user.AddRole(new Role(1, "Administrator", "管理员"));

            // appservice
            var token = await _tokenGenerator.Generate(user);
            var result = new AuthenticateResult(user, token);
            //await _signInManager.SignInAsync(user, false);
            return Ok(new ResultDto<AuthenticateResult>(result));
        }
        /// <summary>
        /// 授权（获取用户有权限的url）
        /// </summary>
        /// <returns></returns>
        [HttpGet("auth")]
        [Authorize(Policy = "Manage")]
        [ProducesResponseType(typeof(ResultDto<Domain.Identity.ActionResult>), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [NoEncrypt]
        public async Task<IActionResult> Auth()
        {

            var identity = new ClaimsIdentity("BearerIdentity");
            identity.AddClaims(User.Claims);

            var ju = UserClaimHelper.JWT2User(identity);
            var user = await _userDomainService.FindAsync(ju.Account!);
            return Ok(new Domain.Identity.ActionResult(user!));
        }
    }
}
