namespace MS.Microservice.Core.Functional
{
    /// <summary>
    /// Either.Left 分支中承载的结构化错误数据。
    /// </summary>
    /// <param name="Code">稳定的错误编码，便于跨层识别错误类别。</param>
    /// <param name="Message">面向调用方的错误摘要。</param>
    /// <param name="Details">补充细节，可用于校验错误、持久化失败原因等。</param>
    public sealed record Error(string Code, string Message, IReadOnlyList<string>? Details = null)
    {
        public IReadOnlyList<string> DetailsOrEmpty => Details ?? [];

        public string ToDisplayMessage()
            => DetailsOrEmpty.Count == 0
                ? Message
                : $"{Message}：{string.Join("；", DetailsOrEmpty)}";

        public static Error Validation(string message, IReadOnlyList<string>? details = null)
            => new("validation", message, details);

        public static Error Conflict(string message, IReadOnlyList<string>? details = null)
            => new("conflict", message, details);

        public static Error Unauthorized(string message, IReadOnlyList<string>? details = null)
            => new("unauthorized", message, details);

        public static Error Unexpected(string message, IReadOnlyList<string>? details = null)
            => new("unexpected", message, details);

        public static Error FromException(Exception exception, string code = "unexpected")
            => new(code, exception.Message, [$"{exception.GetType().Name}: {exception.Message}"]);
    }
}
