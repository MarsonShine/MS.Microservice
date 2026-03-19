using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;

namespace MS.Microservice.Infrastructure.Utils.Excel
{
    public interface IExcelImport
    {
        List<T> Import<T>(byte[] data) where T : class, new();
        List<T> Import<T>(string fileName, byte[] data) where T : class, new();
        List<T> Import<T>(string fileName, Stream stream) where T : class, new();
    }

    public interface IAsyncExcelImport
    {
        ValueTask<List<T>> ImportAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class, new();
        ValueTask<List<T>> ImportAsync<T>(string fileName, byte[] data, CancellationToken cancellationToken = default) where T : class, new();
        ValueTask<List<T>> ImportAsync<T>(string fileName, Stream stream, CancellationToken cancellationToken = default) where T : class, new();
        ValueTask<List<T>> ImportAsync<T>(string fileName, PipeReader reader, CancellationToken cancellationToken = default) where T : class, new();
    }

    public interface IExcelExport
    {
        byte[] Export<T>(List<T> source, string sheetName);
        void Export<T>(IReadOnlyList<T> source, string sheetName, Stream destination);
    }

    public interface IAsyncExcelExport
    {
        ValueTask ExportAsync<T>(IReadOnlyList<T> source, string sheetName, Stream destination, CancellationToken cancellationToken = default);
        ValueTask ExportAsync<T>(IReadOnlyList<T> source, string sheetName, PipeWriter destination, CancellationToken cancellationToken = default);
    }
}
