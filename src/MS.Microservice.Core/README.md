
### HTTP 请求辅助

LogHttpClient 的 GET 参数可为公开可读属性对象或 IDictionary；null 值省略，空字符串保留，
集合展开为同名参数，数值使用 invariant culture，日期使用往返格式。键和值分别 URL 编码，
追加参数时保留已有查询串和片段。POST 使用 UTF-8 application/json。
传入的请求头仅属于本次请求，不修改 HttpClient.DefaultRequestHeaders。

取消、HTTP 状态失败和 JSON 解析失败分别保留 OperationCanceledException、
HttpRequestException、JsonException；调用方应更新旧的“统一解析异常”捕获逻辑。
默认日志不记录 URL、参数、正文及异常消息。
