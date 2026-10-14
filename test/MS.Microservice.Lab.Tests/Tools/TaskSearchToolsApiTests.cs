using System.Net;
using System.Net.Http.Json;
using System.Text;
using MS.Microservice.Lab.Controller;
using MS.Microservice.Search;
using MS.Microservice.Tasks;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class TaskSearchToolsApiTests
{
    [Fact]
    public async Task UploadedTextIsIndexedByCoravelAndSearchIsPrivateAndVersioned()
    {
        using var host = new ToolDemoTestHost(includeTaskCenter: true);
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("财务报告：revenue increased in 2026。")), "File", "report.txt");
        using var uploaded = await host.Client.PostAsync("/api/lab/tools/files", upload);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var file = (await uploaded.Content.ReadFromJsonAsync<DemoStoredFile>())!;
        var request = new IndexDocumentRequest { FileKey = file.Key, DocumentId = "report", FileName = "report.txt", LogicalPath = "/finance/report.txt" };
        using var submitted = await host.Client.PostAsJsonAsync("/api/lab/tools/tasks/documents/index", request);
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        var job = (await submitted.Content.ReadFromJsonAsync<DemoJob>())!;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (job.State is JobState.Queued or JobState.Running && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            job = (await host.Client.GetFromJsonAsync<DemoJob>($"/api/lab/tools/tasks/{job.JobId}"))!;
        }
        Assert.True(job.State == JobState.Succeeded, $"{job.State}: {job.ErrorCode}");
        var dashboardJob = (await host.Client.GetFromJsonAsync<MS.Microservice.Tasks.Dashboard.DashboardJob>($"/task-center/api/jobs/{job.JobId}"))!;
        Assert.Equal(job.JobId, dashboardJob.JobId);
        Assert.Equal(JobState.Succeeded, dashboardJob.State);
        var plans = (await host.Client.GetFromJsonAsync<MS.Microservice.Tasks.Dashboard.DashboardSchedule[]>("/task-center/api/schedules"))!;
        Assert.Equal("持久任务派发", Assert.Single(plans).Name);
        var fast = (await host.Client.GetFromJsonAsync<SearchResult>("/api/lab/tools/search?query=revenue&target=Content&snippetLength=100"))!;
        Assert.Single(fast.Items);
        Assert.Equal(SearchSort.Path, fast.Sort);
        Assert.Null(fast.TotalHits);
        Assert.Contains("revenue", fast.Items[0].Snippet);
        var exact = (await host.Client.GetFromJsonAsync<SearchResult>("/api/lab/tools/search?query=report&countMode=Exact"))!;
        Assert.Equal(1, exact.TotalHits);
        host.Client.DefaultRequestHeaders.Remove("X-Test-User");
        host.Client.DefaultRequestHeaders.Add("X-Test-User", "bob");
        Assert.Empty((await host.Client.GetFromJsonAsync<SearchResult>("/api/lab/tools/search?query=report"))!.Items);
        using var otherJob = await host.Client.GetAsync($"/api/lab/tools/tasks/{job.JobId}");
        Assert.Equal(HttpStatusCode.NotFound, otherJob.StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-Test-User");
        host.Client.DefaultRequestHeaders.Add("X-Test-User", "alice");
        using var deleted = await host.Client.DeleteAsync("/api/lab/tools/search/report?version=2");
        deleted.EnsureSuccessStatusCode();
        Assert.Empty((await host.Client.GetFromJsonAsync<SearchResult>("/api/lab/tools/search?query=report"))!.Items);
        using var original = await host.Client.GetAsync(file.DownloadUrl);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
    }
}
