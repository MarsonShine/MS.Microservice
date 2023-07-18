using System.Collections.Generic;
using System.IO;

namespace MS.Microservice.Infrastructure.Utils
{
    public interface IExcelImport
    {
        List<T> Import<T>(byte[] data);
        List<T> Import<T>(string fileName, byte[] data);
        List<T> Import<T>(string fileName, Stream stream);
    }

    public interface IExcelExport
    {
        byte[] Export<T>(List<T> source, string sheetName);
    }
}
