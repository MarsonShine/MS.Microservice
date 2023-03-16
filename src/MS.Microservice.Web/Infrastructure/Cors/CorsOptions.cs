using System;

namespace MS.Microservice.Web.Infrastructure.Cors
{
    public class CorsOptions
    {
        public bool IsEnabled { get; set; }
        public string PolicyName { get; set; } = "MS.Microservice.Web";
        public string[] Origins { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 全部跨域
        /// </summary>
        public bool IsAllCors { get; set; }
    }
}
