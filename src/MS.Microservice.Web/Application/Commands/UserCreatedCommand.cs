using MediatR;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace MS.Microservice.Web.Application.Commands
{
    public class UserCreatedCommand : IRequest<(bool, string?)>
    {
        private string _password;

        [JsonConstructor]
        public UserCreatedCommand(string account, string userName, string passowrd, string telephone, string email, List<RoleDto> roles)
        {
            Account = account;
            UserName = userName;
            _password = passowrd;
            Telephone = telephone;
            Email = email;
            Roles = roles ?? new List<RoleDto>();
        }

        /// <summary>
        /// 账号
        /// </summary>
        [JsonInclude]
        public string Account { get; private set; }
        /// <summary>
        /// 用户名称
        /// </summary>
        [JsonInclude]
        public string UserName { get; private set; }
        /// <summary>
        /// 密码
        /// </summary>
        [JsonInclude]
        public string Passowrd
        {
            get
            {
                var base64Buffer = Convert.FromBase64String(_password);
                return Encoding.UTF8.GetString(base64Buffer);
            }
            private set
            {
                _password = value;
            }
        }
        /// <summary>
        /// 电话
        /// </summary>
        [JsonInclude]
        public string Telephone { get; private set; }
        /// <summary>
        /// 邮箱
        /// </summary>
        [JsonInclude]
        public string Email { get; private set; }
        /// <summary>
        /// 角色
        /// </summary>
        [JsonInclude]
        public List<RoleDto> Roles { get; private set; }
    }

    public class RoleDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
