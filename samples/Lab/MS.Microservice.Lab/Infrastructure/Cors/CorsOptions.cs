using System;

namespace MS.Microservice.Lab.Infrastructure.Cors
{
    public class CorsOptions
    {
        public const string SectionName = "CorsOptions";

        public bool IsEnabled { get; set; }
        public string PolicyName { get; set; } = "MS.Microservice.Lab";
		public string[] Origins { get; set; } = [];

        /// <summary>
        /// 全部跨域
        /// </summary>
        public bool IsAllCors { get; set; }
    }
}
