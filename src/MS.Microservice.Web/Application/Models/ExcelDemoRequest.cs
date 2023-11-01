using MS.Microservice.Infrastructure.Utils;
using System.Collections.Generic;
using System.Linq;

namespace MS.Microservice.Web.Application.Models
{
    public class ExcelDemoRequest
    {
        [ExcelColumn("主键")]
        public int BookId { get; set; }
        [ExcelColumn("分类")]
        public string? Classify { get; set; }

        public int[] BookClassify => Classify!.Split('、')
            .Select(n => KeyValues[n])
            .ToArray();

        Dictionary<string, int> KeyValues = new Dictionary<string, int>()
        {
            {"1",1 },
            {"2",2 },
            {"3",3 },
            {"4",4 }
        };
    }
}
