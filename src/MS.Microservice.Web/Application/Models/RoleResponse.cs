
namespace MS.Microservice.Web.Application.Models
{
    public class RoleResponse
    {
        public int RoleId { get; set; }
        /// <summary>
        /// 角色名称
        /// </summary>
        public string? Name { get; set; }
        /// <summary>
        /// 描述
        /// </summary>
        public string? Description { get; set; }
    }
}
