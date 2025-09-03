using System.Collections.Generic;
using System.IO;

namespace MS.Microservice.Infrastructure.Utils.Excel
{
    public interface IExcelImport
    {
        List<T> Import<T>(byte[] data) where T : class, new();
        List<T> Import<T>(string fileName, byte[] data) where T : class, new();
        List<T> Import<T>(string fileName, Stream stream) where T : class, new();
        IEnumerable<T> ImportAsEnumerable<T>(string fileName, Stream stream) where T : class, new();
    }

    public interface IExcelExport
    {
        byte[] Export<T>(List<T> source, string sheetName);
        void ExportToStream<T>(List<T> source, string sheetName, Stream destination);
    }
}
