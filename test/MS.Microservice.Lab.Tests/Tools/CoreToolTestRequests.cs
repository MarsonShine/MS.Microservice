using System.Net;
using System.Net.Http.Json;

namespace MS.Microservice.Lab.Tests.Tools;

internal static class CoreToolTestRequests
{
    internal static MultipartFormDataContent Form(string field, string name, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), field, name);
        return form;
    }

    internal static async Task<T> Json<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK) where T : class
    {
        await Status(response, expected);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    internal static async Task Status(HttpResponseMessage response, HttpStatusCode expected)
        => Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
}
