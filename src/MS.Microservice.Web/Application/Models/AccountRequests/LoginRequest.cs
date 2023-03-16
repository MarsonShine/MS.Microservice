using System.ComponentModel.DataAnnotations;

namespace MS.Microservice.Web.Application.Models.AccountRequests
{
    public class LoginRequest
    {
        /// <summary>
        /// 账号
        /// </summary>
        [Required]
        public string Account { get; set; }
        /// <summary>
        /// 密码
        /// </summary>
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }
}
