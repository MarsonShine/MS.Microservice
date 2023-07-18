using MS.Microservice.Core.Serialization.Converters;
using System.Text.Json.Serialization;

namespace MS.Microservice.Web.Application.Models
{
    public class UserPagedResponse
    {
        public bool IsDisabled { get; set; }
        /// <summary>
        /// 手机
        /// </summary>
        [JsonConverter(typeof(PhoneDesensitizationConverter))]
        public string? Telephone { get; set; }
        /// <summary>
        /// 操作人
        /// </summary>
        public int CreatorId { get; set; }
        /// <summary>
        /// 更新人
        /// </summary>
        public int UpdatorId { get; set; }
        /// <summary>
        /// Email
        /// </summary>
        public string? Email { get; set; }
        /// <summary>
        /// 用户姓名
        /// </summary>
        public string? Name { get; set; }
        /// <summary>
        /// 用户账号
        /// </summary>
        public string? Account { get; set; }
        /// <summary>
        /// 方直账号
        /// </summary>
        public string? FzAccount { get; set; }
        /// <summary>
        /// 方直id
        /// </summary>
        public string? FzId { get; set; }
        
        public string[]? Role { get; set; }

    }
}
