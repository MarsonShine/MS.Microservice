using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;

namespace MS.Microservice.Excel.Aot
{
    public interface IExcelImport
    {
        List<T> Import<T>(byte[] data, ExcelModelMap<T> map) where T : class;
        List<T> Import<T>(string fileName, byte[] data, ExcelModelMap<T> map) where T : class;
        List<T> Import<T>(string fileName, Stream stream, ExcelModelMap<T> map) where T : class;
    }

    public interface IAsyncExcelImport
    {
        ValueTask<List<T>> ImportAsync<T>(byte[] data, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class;
        ValueTask<List<T>> ImportAsync<T>(string fileName, byte[] data, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class;
        ValueTask<List<T>> ImportAsync<T>(string fileName, Stream stream, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class;
        ValueTask<List<T>> ImportAsync<T>(string fileName, PipeReader reader, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class;
    }

    public interface IExcelExport
    {
        byte[] Export<T>(List<T> source, string sheetName, ExcelModelMap<T> map) where T : class;
        void Export<T>(IReadOnlyList<T> source, string sheetName, Stream destination, ExcelModelMap<T> map) where T : class;
    }

    public interface IAsyncExcelExport
    {
        ValueTask ExportAsync<T>(IReadOnlyList<T> source, string sheetName, Stream destination, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class;
        ValueTask ExportAsync<T>(IReadOnlyList<T> source, string sheetName, PipeWriter destination, ExcelModelMap<T> map, CancellationToken cancellationToken = default) where T : class;
    }
}
