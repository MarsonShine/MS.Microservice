using System.Data.Common;
using MS.Microservice.Messaging.SelfManaged;
using Xunit;
using static MS.Microservice.Messaging.SelfManaged.Tests.UnitOfWorkTests;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class StorageProbeTests
{
    [Fact]
    public async Task ProbeRequiresMessageTablesAndNeverCreatesThem()
    {
        await using var connection = await OpenAsync();
        await using var context = new BusinessContext(connection);
        var probe = new SelfManagedStorageProbe<BusinessContext>(context);
        await Assert.ThrowsAnyAsync<DbException>(() => probe.CheckAsync(default));
        await context.Database.EnsureCreatedAsync();
        await probe.CheckAsync(default);
    }
}
