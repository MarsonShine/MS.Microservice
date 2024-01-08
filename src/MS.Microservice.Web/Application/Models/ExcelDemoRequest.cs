using MS.Microservice.Core.Extension;
using MS.Microservice.Infrastructure.Utils.Excel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MS.Microservice.Web.Application.Models
{
    public class ExcelDemoRequest
    {
        [ExcelColumn("BOOKID")]
        public int BookId { get; set; }
        [ExcelColumn("分类")]
        public string? Classify { get; set; }

        public BookClassifyEnum[] BookClassify => Classify.IsNullOrEmpty() ? Array.Empty<BookClassifyEnum>() : Classify!.Split('、')
            .Select(n => KeyValues.TryGetValue(n, out var value) ? value : (BookClassifyEnum)(-1))
            .ToArray();

        Dictionary<string, BookClassifyEnum> KeyValues = new Dictionary<string, BookClassifyEnum>()
        {
            {"数字教辅",BookClassifyEnum.TeachingAssistant },
            {"在线阅读",BookClassifyEnum.OnlineReading },
            {"课本配套",BookClassifyEnum.TeachingMaterial },
            {"教师研修",BookClassifyEnum.TeacherTraining }
        };

        public enum BookClassifyEnum
        {
            None = 0,
            /// <summary>
            /// 数字教辅 - 原学生资源
            /// </summary>
            TeachingAssistant = 1,
            /// <summary>
            /// 课本配套
            /// </summary>
            TeachingMaterial = 2,
            /// <summary>
            /// 在线阅读 - 原在线点读
            /// </summary>
            OnlineReading = 3,
            /// <summary>
            /// 教师研修 - 原教师资源
            /// </summary>
            TeacherTraining = 4
        }
    }
}
