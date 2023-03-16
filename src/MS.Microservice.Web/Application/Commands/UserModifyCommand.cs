using MediatR;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace MS.Microservice.Web.Application.Commands
{
    public class UserModifyCommand : IRequest<(bool, string)>
    {
        private string _password;

        [JsonConstructor]
        public UserModifyCommand(string account, string userName, string passowrd, string telephone, string email, List<RoleDto> roles)
        {
            Account = account;
            UserName = userName;
            _password = passowrd;
            Telephone = telephone;
            Email = email;
            if (roles != null)
            {
                Roles = roles;
            }

        }
        
        /// <summary>
        /// 账号（必填）
        /// </summary>
        [JsonInclude]
        public string Account { get; private set; }

        /// <summary>
        /// 名称（选填，不填不更新）
        /// </summary>
        [JsonInclude]
        public string UserName { get; private set; }
        /// <summary>
        /// 密码（选填，不填不更新）
        /// </summary>
        [JsonInclude]
        public string Passowrd
        {
            get
            {
                if (_password == null)
                    return "";
                var base64Buffer = Convert.FromBase64String(_password);
                return Encoding.UTF8.GetString(base64Buffer);
            }
            private set
            {
                _password = value;
            }
        }

        /// <summary>
        /// 电话（选填，不填不更新）
        /// </summary>
        [JsonInclude]
        public string Telephone { get; private set; }

        /// <summary>
        /// 邮箱（选填，不填不更新）
        /// </summary>
        [JsonInclude]
        public string Email { get; private set; }

        /// <summary>
        /// 角色（必填，会根据前端出入值同步更新（增删同步））
        /// </summary>
        [JsonInclude]
        public List<RoleDto> Roles { get; private set; }
    }
}
