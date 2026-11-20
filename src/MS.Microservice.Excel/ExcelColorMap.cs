using NPOI.HSSF.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MS.Microservice.Infrastructure.Utils.Excel
{
    public class ExcelColorMap
    {
        private static readonly Dictionary<string, short> DefaultColors = typeof(HSSFColor).GetNestedTypes()
            .Where(typeof(HSSFColor).IsAssignableFrom)
            .ToDictionary(type => type.Name, type => (short)type.GetField("Index")!.GetValue(null)!);

        // The public map is mutable. Share the reflection result and give each caller its own dictionary.
        public Dictionary<string, short>? Colors = new(DefaultColors);

        public bool TryGetColor(string color, out short value)
        {
            return Colors!.TryGetValue(color, out value);
        }
    }
}
