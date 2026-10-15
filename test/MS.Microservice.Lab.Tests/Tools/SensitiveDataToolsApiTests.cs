using System.Net;
using System.Net.Http.Json;
using MS.Microservice.Lab.Controller;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class SensitiveDataToolsApiTests
{
    [Fact]
    public async Task PreviewExportAndAuditedChangesProtectValuesAndKeepOwnerIsolation()
    {
        using var host = new ToolDemoTestHost(includeSensitiveData: true);
        var input = new SensitiveDemoRequest { InternalNote = "never-output-original" };
        using var preview = await host.Client.PostAsJsonAsync("/api/lab/tools/sensitive-data/preview", input);
        preview.EnsureSuccessStatusCode(); var text = await preview.Content.ReadAsStringAsync();
        Assert.DoesNotContain(input.InternalNote, text); Assert.DoesNotContain(input.Phone, text); Assert.Contains("138****8000", text);
        using var csv = await host.Client.PostAsJsonAsync("/api/lab/tools/sensitive-data/export", input);
        csv.EnsureSuccessStatusCode(); var content = await csv.Content.ReadAsStringAsync();
        Assert.DoesNotContain(input.InternalNote, content); Assert.DoesNotContain("InternalNote", content); Assert.Contains("a***@example.com", content);
        using var created = await host.Client.PostAsJsonAsync("/api/lab/tools/sensitive-data/records", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = (await created.Content.ReadFromJsonAsync<SensitiveWriteResult>())!;
        input.Phone = "13899118000"; input.InternalNote = "never-output-updated";
        using var update = await host.Client.PutAsJsonAsync($"/api/lab/tools/sensitive-data/records/{first.Record.Id}", input);
        update.EnsureSuccessStatusCode(); var updated = (await update.Content.ReadFromJsonAsync<SensitiveWriteResult>())!;
        using var audit = await host.Client.GetAsync($"/api/lab/tools/sensitive-data/operations/{updated.OperationId}");
        audit.EnsureSuccessStatusCode(); var auditText = await audit.Content.ReadAsStringAsync();
        Assert.DoesNotContain(input.Phone, auditText); Assert.DoesNotContain("never-output", auditText);
        var records = (await audit.Content.ReadFromJsonAsync<AuditDemoRecord[]>())!;
        Assert.Single(records); Assert.Equal(2, records[0].Changes.GetArrayLength());
        Assert.Equal("Masked", records[0].Changes[0].GetProperty("protection").GetString());
        Assert.Equal("ChangedOnly", records[0].Changes[1].GetProperty("protection").GetString());
        Assert.False(records[0].Changes[1].TryGetProperty("after", out _));
        host.Client.DefaultRequestHeaders.Remove("X-Test-User"); host.Client.DefaultRequestHeaders.Add("X-Test-User", "bob");
        using var other = await host.Client.GetAsync($"/api/lab/tools/sensitive-data/operations/{updated.OperationId}");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }
}
