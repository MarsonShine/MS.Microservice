using System.Net.Http.Headers;

namespace MS.Microservice.Lab.Tests.Tools;

internal static class ToolDemoHttpFiles
{
    internal static async Task<byte[]> PostFileAsync(HttpClient client, string route, byte[] bytes, string filename, string mime,
        Dictionary<string, string>? fields = null)
    {
        using var form = new MultipartFormDataContent();
        AddFile(form, bytes, "File", filename, mime);
        if (fields is not null) foreach (var (name, value) in fields) form.Add(new StringContent(value), name);
        using var response = await client.PostAsync(route, form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    internal static void AddFile(MultipartFormDataContent form, byte[] bytes, string name, string filename, string mime)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(content, name, filename);
    }
}
