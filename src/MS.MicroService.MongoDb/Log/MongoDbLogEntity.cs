using MS.Microservice.Domain;
using System;
using System.Diagnostics.CodeAnalysis;

namespace MS.MicroService.MongoDb.Log
{
    public class MongoDbLogEntity : BaseEntity
    {
        public MongoDbLogEntity(int id) : base(id)
        {
        }

        /// <summary>
        /// 操作人姓名
        /// </summary>
        public string? UserName { get; set; }
        /// <summary>
        /// 操作人Id
        /// </summary>
        public long UserId { get; set; }
        /// <summary>
        /// 日志内容
        /// </summary>
        public string? Content { get; set; }
        /// <summary>
        /// 日志时间
        /// </summary>
        public DateTime LogDateTime { get; set; }
        public string? IP { get; set; }
        /// <summary>
        /// 操作来源
        /// </summary>
        public string? SourceFrom { get; set; }
        public string? CategoryName { get; set; }
    }
}