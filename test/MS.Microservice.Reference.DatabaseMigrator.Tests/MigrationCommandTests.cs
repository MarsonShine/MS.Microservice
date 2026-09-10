using System.Security.Cryptography;
using System.Text.Json;
using MS.Microservice.Reference.DatabaseMigrator;
using Xunit;

namespace MS.Microservice.Reference.DatabaseMigrator.Tests;

public sealed class MigrationCommandTests
{
    [Theory]
    [InlineData("SelfManaged")]
    [InlineData("Wolverine")]
    public async Task DefaultExportDoesNotApplyAndProducesVerifiableArtifacts(string provider)
    {
        var path = Path.Combine(Path.GetTempPath(), "ms-reference-migrations-" + Guid.NewGuid().ToString("N"));
        try
        {
            var command = MigrationCommand.Parse(["--provider", provider, "--output", path]);
            Assert.False(command.Apply);
            Assert.False(command.ProvisionBroker);
            await Program.RunAsync(command);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path, "manifest.json")));
            foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(path, file.GetProperty("name").GetString()!));
                Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)));
            }
            if (provider == "Wolverine")
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(path, "wolverine-messaging-create.sql"));
                Assert.Contains("wolverine_dead_letters", sql);
                Assert.Contains("wolverine_nodes", sql);
                Assert.Contains("PRIMARY KEY (id, received_at)", sql);
                Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Temporary output escaped its expected root.");
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
        }
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--provider")]
    [InlineData("--output")]
    public void UnknownOrIncompleteOptionsCannotTriggerWork(string option)
        => Assert.Throws<ArgumentException>(() => MigrationCommand.Parse([option]));

    [Fact]
    public void DestructiveActionsRequireExplicitFlags()
    {
        var command = MigrationCommand.Parse(["--provider", "SelfManaged", "--apply", "--provision-broker"]);
        Assert.True(command.Apply);
        Assert.True(command.ProvisionBroker);
        Assert.Throws<ArgumentException>(() => MigrationCommand.Parse(["--provider", "unknown"]));
        Assert.Throws<ArgumentException>(() => MigrationCommand.Parse(["--output", "--apply"]));
    }
}
