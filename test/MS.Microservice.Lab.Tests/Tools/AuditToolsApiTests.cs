using System.Net;
using System.Net.Http.Json;
using MS.Microservice.Lab.Controller;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class AuditToolsApiTests
{
    [Fact]
    public async Task TaggedApprovalAutomaticallyGroupsThreeTablesAndKeepsAuditPrivate()
    {
        using var host = new ToolDemoTestHost(includeAuditing: true);
        using var created = await host.Client.PostAsJsonAsync("/api/lab/tools/audits/orders", new CreateAuditOrderRequest { Quantity = 3 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<AuditDemoResult>())!;
        using var response = await host.Client.PostAsync($"/api/lab/tools/audits/orders/{order.Order.Id}/approve", null);
        response.EnsureSuccessStatusCode();
        var approved = (await response.Content.ReadFromJsonAsync<AuditDemoResult>())!;
        Assert.NotEqual(order.OperationId, approved.OperationId);
        Assert.Equal("Approved", approved.Order.Status);
        Assert.All(approved.Order.Stock, value => Assert.Equal(97, value.Available));
        var records = (await host.Client.GetFromJsonAsync<AuditDemoRecord[]>($"/api/lab/tools/audits/operations/{approved.OperationId}"))!;
        Assert.Equal(4, records.Length);
        Assert.Equal(new[] { "approval", "order", "stock", "stock" }, records.Select(value => value.ResourceType));
        Assert.All(records, value => { Assert.Equal(1, value.SaveSequence); Assert.Equal("alice", value.Actor); Assert.Equal("lab.order.approve", value.Action); });
        var status = Assert.Single(records, value => value.ResourceType == "order").Changes[0];
        Assert.Equal("Pending", status.GetProperty("before").GetString());
        Assert.Equal("Approved", status.GetProperty("after").GetString());
        host.Client.DefaultRequestHeaders.Remove("X-Test-User"); host.Client.DefaultRequestHeaders.Add("X-Test-User", "bob");
        using var other = await host.Client.GetAsync($"/api/lab/tools/audits/operations/{approved.OperationId}");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using var otherOrder = await host.Client.GetAsync($"/api/lab/tools/audits/orders/{order.Order.Id}");
        Assert.Equal(HttpStatusCode.NotFound, otherOrder.StatusCode);
    }
}
