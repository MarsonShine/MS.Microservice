using Microsoft.Data.Sqlite;
using Xunit;

namespace MS.Microservice.Persistence.SqlSugar.Tests;

public sealed class SqliteRuntimeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(100)]
    public async Task BundledRuntime_IsPatched_AndExecutesAggregateQueries(int count)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "select sqlite_version()";
        var version = Version.Parse((string)(await versionCommand.ExecuteScalarAsync())!);
        Assert.True(version >= new Version(3, 50, 2), $"Vulnerable SQLite native runtime: {version}");

        await using var command = connection.CreateCommand();
        command.CommandText = "with recursive n(x) as (select 1 where $count > 0 union all select x + 1 from n where x < $count) select count(*), sum(x), max(x), min(x) from n";
        command.Parameters.AddWithValue("$count", count);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(count, reader.GetInt64(0));
        if (count > 0) Assert.Equal((long)count * (count + 1) / 2, reader.GetInt64(1));
        else Assert.True(reader.IsDBNull(1));
    }
}
