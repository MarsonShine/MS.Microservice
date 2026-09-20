using Microsoft.Extensions.Logging;

namespace MS.Microservice.Lab.AotExamples.Static;

/// <summary>异步读取启用的正文日志；日志关闭时保持原始内容的流式传输。</summary>
public class LoggingHttpClientHandler(ILogger<LoggingHttpClientHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!logger.IsEnabled(LogLevel.Information))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var guid = Guid.NewGuid();
        // 正文 IO 留在异步发送流程；日志参数只保存字符串，避免格式化时同步等待读取。
        var payload = await ReadContentForLogAsync(request.Content, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "【{Guid}】method:【{Method}】log request: 【{RequestUri}】 payload: 【{@Payload}】",
            guid, request.Method.Method, request.RequestUri, payload);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var responseBody = await ReadContentForLogAsync(response.Content, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "【{Guid}】method:【{Method}】log response: 【{@Response}】",
                guid, request.Method.Method, responseBody);
            return response;
        }
        catch
        {
            // 调用方尚未取得响应；日志读取取消或日志提供器失败时由这里释放。
            response.Dispose();
            throw;
        }
    }

    private static async Task<string> ReadContentForLogAsync(HttpContent? content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content is null) return "null";
        try
        {
            // 原 HttpContent 内部缓冲字节供后续发送/复制复用，因此无需额外内容包装器。
            var text = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return $"Error reading content: {exception.Message}";
        }
    }
}
