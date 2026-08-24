using NPOI.HSSF.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MS.Microservice.Infrastructure.Utils.Excel
{
    public class ExcelColorMap
    {
        public Dictionary<string, short>? Colors;

        public ExcelColorMap()
        {
            Init();
        }

        private void Init()
        {
            var nestedTypes = typeof(HSSFColor).GetNestedTypes()
                .Where(p => typeof(HSSFColor).IsAssignableFrom(p))
                .ToArray();
            if (nestedTypes.Length == 0)
                return;

            Colors = new Dictionary<string, short>(nestedTypes.Length);
            foreach (var t in nestedTypes)
            {
                var obj = Activator.CreateInstance(t);
                var key = t.Name;
                var value = (short)t!.GetField("Index")!.GetValue(obj)!;
                Colors.Add(key, value);
            }
        }

        public bool TryGetColor(string color, out short value)
        {
            return Colors!.TryGetValue(color, out value);
        }
    }
}
