using System.Text;
using System.Text.Json;
using AlibabaCloud.OpenApiClient.Models;
using AlibabaCloud.SDK.Dysmsapi20170525;
using AlibabaCloud.SDK.Dysmsapi20170525.Models;
using AlibabaCloud.TeaUtil.Models;

namespace MS.Microservice.Notifications.Sms;

/// <summary>Submits one template SMS through the official SDK, without automatic retries.</summary>
public sealed class AliyunSmsSender
{
    private readonly Client _client;
    private readonly AliyunSmsOptions _options;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public AliyunSmsSender(AliyunSmsOptions options) : this(options, static config => new Client(config)) { }

    internal AliyunSmsSender(AliyunSmsOptions options, Func<Config, Client> createClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createClient);
        ValidateText(options.AccessKeyId, 256, nameof(options.AccessKeyId));
        ValidateText(options.AccessKeySecret, 4096, nameof(options.AccessKeySecret));
        if (options.SecurityToken is not null) ValidateText(options.SecurityToken, 16_384, nameof(options.SecurityToken));
        if (options.TimeoutMilliseconds is < 1 or > 100_000 || options.MaximumTemplateBytes < 1)
            throw new ArgumentException("Use a 1–100,000 ms request timeout and a positive template byte limit.", nameof(options));
        _options = options;
        _client = createClient(new Config
        {
            Credential = new Aliyun.Credentials.Client(new Aliyun.Credentials.Models.Config
            {
                Type = options.SecurityToken is null ? "access_key" : "sts", AccessKeyId = options.AccessKeyId,
                AccessKeySecret = options.AccessKeySecret, SecurityToken = options.SecurityToken
            }),
            Endpoint = "dysmsapi.aliyuncs.com", Protocol = "https",
            RegionId = "cn-hangzhou", ConnectTimeout = 0, ReadTimeout = options.TimeoutMilliseconds,
            EnableUsageDataCollection = false
        });
    }

    /// <summary>Returns after provider acceptance. The SDK has no in-flight cancellation API; TimeoutMilliseconds bounds its HTTP request.</summary>
    public async Task<SmsAcceptanceReceipt> SendAsync(SmsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.PhoneNumber);
        var digits = message.PhoneNumber.AsSpan();
        if (digits[0] == '+') digits = digits[1..];
        if (digits.Length is < 6 or > 20 || digits.ContainsAnyExcept("0123456789"))
            throw new ArgumentException("Use one phone number containing 6–20 ASCII digits and an optional leading plus.", nameof(message));
        ValidateText(message.SignName, 256, nameof(message.SignName));
        ValidateText(message.TemplateCode, 128, nameof(message.TemplateCode));
        if (!message.TemplateCode.StartsWith("SMS_", StringComparison.Ordinal)
            || message.TemplateCode.Length == 4 || message.TemplateCode.AsSpan(4).ContainsAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"))
            throw new ArgumentException("Use an approved SMS_ template code.", nameof(message));
        if (message.OutId is not null) ValidateText(message.OutId, 256, nameof(message.OutId));
        string? parameters = null;
        if (message.TemplateParameters is { Count: > 0 } values)
        {
            if (values.Count > 32) throw new ArgumentException("At most 32 template variables are supported.", nameof(message));
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                ValidateText(pair.Key, 64, "template variable name");
                ArgumentNullException.ThrowIfNull(pair.Value);
                if (pair.Value.Length > 2048) throw new ArgumentException("A template value is too long.", nameof(message));
                StrictUtf8.GetByteCount(pair.Value);
                snapshot.Add(pair.Key, pair.Value);
            }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
            if (bytes.Length > _options.MaximumTemplateBytes) throw new ArgumentException("Template JSON exceeds its encoded byte limit.", nameof(message));
            parameters = Encoding.UTF8.GetString(bytes);
        }
        var response = await _client.SendSmsWithOptionsAsync(new SendSmsRequest
        {
            PhoneNumbers = message.PhoneNumber, SignName = message.SignName, TemplateCode = message.TemplateCode,
            TemplateParam = parameters, OutId = message.OutId
        }, new RuntimeOptions
        {
            Autoretry = false, MaxAttempts = 1,
            // Tea combines these values into one request timeout; its HttpClient also caps requests at 100 seconds.
            ConnectTimeout = 0, ReadTimeout = _options.TimeoutMilliseconds
        }).ConfigureAwait(false);
        var body = response?.Body ?? throw new InvalidDataException("The SMS provider returned no response body.");
        if (string.IsNullOrWhiteSpace(body.Code)) throw new InvalidDataException("The SMS provider returned no status code.");
        if (!string.Equals(body.Code, "OK", StringComparison.Ordinal))
            throw new SmsRejectedException(body.Code, body.RequestId, body.Message);
        if (string.IsNullOrWhiteSpace(body.RequestId) || string.IsNullOrWhiteSpace(body.BizId))
            throw new InvalidDataException("The accepted SMS response contains no request or business receipt identifier.");
        return new(body.RequestId, body.BizId, DateTimeOffset.UtcNow, message.OutId);
    }

    private static void ValidateText(string value, int maximumLength, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > maximumLength || value.Any(char.IsControl)) throw new ArgumentException("Use bounded text without control characters.", parameter);
        StrictUtf8.GetByteCount(value);
    }
}
