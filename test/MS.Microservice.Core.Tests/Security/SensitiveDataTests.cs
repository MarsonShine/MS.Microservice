using System.Text.Json;
using Microsoft.Extensions.Logging;
using MS.Microservice.Core.Security.SensitiveData;

namespace MS.Microservice.Core.Tests.Security;

public sealed class SensitiveDataTests
{
    [Theory]
    [InlineData("13800138000", SensitiveKind.Phone, "138****8000")]
    [InlineData("+8613800138000", SensitiveKind.Phone, "+86*******8000")]
    [InlineData("alice@example.com", SensitiveKind.Email, "a***@example.com")]
    [InlineData("短号码", SensitiveKind.Phone, SensitiveValues.Redacted)]
    [InlineData("not-email", SensitiveKind.Email, SensitiveValues.Redacted)]
    [InlineData("a@evil.com\r\nnext", SensitiveKind.Email, SensitiveValues.Redacted)]
    [InlineData("", SensitiveKind.Phone, "")]
    [InlineData(null, SensitiveKind.Email, null)]
    [InlineData("raw-secret", SensitiveKind.Secret, null)]
    public void ScalarRulesNeverReturnUnrecognizedSensitiveInput(string? input, SensitiveKind kind, string? expected)
    {
        var result = SensitiveValues.Protect(input, kind);
        Assert.Equal(expected, result);
        if (kind == SensitiveKind.Phone && result is not null && result != SensitiveValues.Redacted)
            Assert.Equal(input!.Length, result.Length);
    }
    [Fact]
    public void JsonAndProjectionProtectOutputWithoutMutatingOrMaskingInput()
    {
        var original = new Sample { Phone = "13800138000", Email = "alice@example.com", Secret = "never-output" };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.UseSensitiveValues();
        var json = JsonSerializer.Serialize(original, options);
        Assert.DoesNotContain("never-output", json); Assert.DoesNotContain("secret", json);
        Assert.Contains("138****8000", json); Assert.Equal("13800138000", original.Phone);
        var incoming = JsonSerializer.Deserialize<Sample>("{\"phone\":\"13800138000\",\"secret\":\"incoming\"}", options)!;
        Assert.Equal("incoming", incoming.Secret); Assert.Equal("13800138000", incoming.Phone);
        var projection = SensitiveValues.Project(original);
        Assert.False(projection.ContainsKey(nameof(Sample.Secret))); Assert.False(projection.ContainsKey(nameof(Sample.Child)));
        Assert.Equal("a***@example.com", projection[nameof(Sample.Email)]);
        var logger = new CaptureLogger(); logger.LogSensitive(LogLevel.Information, "sample", original);
        Assert.DoesNotContain("never-output", logger.Text); Assert.DoesNotContain("13800138000", logger.Text);
        Assert.False(logger.Fields!.ContainsKey(nameof(Sample.Secret)));
    }
    [Fact]
    public void SecretGettersAreNotReadAndDisabledLoggingDoesNotInspectTheDto()
    {
        var logger = new CaptureLogger { Enabled = false };
        logger.LogSensitive(LogLevel.Debug, "ignored", new ThrowingDto());
        var options = new JsonSerializerOptions(); options.UseSensitiveValues();
        Assert.Equal("{}", JsonSerializer.Serialize(new ThrowingDto(), options));
        Assert.Empty(SensitiveValues.Project(new ThrowingDto()));
    }
    public sealed class Sample
    {
        [Sensitive(SensitiveKind.Phone)] public string? Phone { get; set; }
        [Sensitive(SensitiveKind.Email)] public string? Email { get; set; }
        [Sensitive(SensitiveKind.Secret)] public string? Secret { get; set; }
        public object Child { get; set; } = new();
    }
    public sealed class ThrowingDto { [Sensitive(SensitiveKind.Secret)] public string Secret => throw new InvalidOperationException("must not read"); }
    private sealed class CaptureLogger : ILogger
    {
        public bool Enabled = true; public string Text = ""; public Dictionary<string, object?>? Fields;
        public bool IsEnabled(LogLevel level) => Enabled;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Text = formatter(state, exception); Fields = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(); }
    }
}
