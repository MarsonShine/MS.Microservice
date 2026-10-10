# 通知组件：SMTP 邮件与命名模板

本组件提供 SMTP 邮件发送和简单命名模板。邮件回执表示服务器已接受消息，最终送达由邮件服务的投递反馈确认；宿主负责发送任务、状态存储与重试策略。

```csharp
var sender = new SmtpEmailSender(new SmtpEmailOptions
{
    Host = "smtp.example.com",
    Port = 587,
    TlsMode = SmtpTlsMode.RequireStartTls,
    UserName = configuredUserName,
    Password = configuredPassword
});

var body = NamedTokenTemplate.Render("你好，{{User.Name}}。", values);
var accepted = await sender.SendAsync(new EmailMessage
{
    From = new("sender@example.com", "应用通知"),
    To = [new("recipient@example.com")],
    Subject = "处理完成",
    TextBody = body
}, cancellationToken);
```

公开类型不暴露 MailKit/MimeKit。发送支持 From、To、Cc、Bcc、纯文本、HTML 或两种正文的 MIME alternative；传输遵循 MIME/SMTP 换行规则，单一正文在接收端可能带有末尾 CRLF。至少一个收件人和一种正文必须存在；允许仅有 Bcc，Bcc 进入 SMTP 信封但不会进入发送的 MIME 头。邮箱须是单一 mailbox，显示名独立传入；主题和地址头不允许 CR、LF 或 NUL。服务器决定地址是否可投递。

TLS 默认明确要求 STARTTLS；服务器不支持时失败，不降级。也可选择连接即 TLS，或明确的无 TLS 模式。验证使用系统默认服务器证书规则，不接受所有证书；凭据仅允许随 TLS 配置使用，库不记录密码，配置类型没有输出密码的自动 record 字符串。认证通过 MailKit 支持的用户名/密码机制，凭据由宿主提供。邮件组件没有 OAuth token 获取、附件或 MIME 自定义接口。

每次发送使用独立客户端，不共享连接和并发状态。返回 `SmtpAcceptanceReceipt` 表示收到服务器对消息的成功 SMTP 响应，包含本地 Message-Id、原始响应和接受时间；该响应不证明邮箱送达、用户读取或所有后续转发成功。

接受后使用独立、可配置的有限超时尽力断开连接。QUIT 失败、断开超时或此时调用方取消均不会把已接受邮件变成发送失败，应用不应据此重发。MailKit 本身会容忍 QUIT 的部分网络和协议错误，回执不保证收到 QUIT 确认，也不包含用于应用决策的清理状态。接受前的连接、认证、收件人、消息拒绝或取消异常直接传播，清理异常不覆盖原错误。网络中断或等待最终接受响应时取消，远端是否已接受可能无法确定；组件不自动重试，宿主应明确重复发送策略，不仅凭异常就假定邮件未发送。`TimeoutMilliseconds` 控制客户端 I/O 等待，调用方取消控制整个发送操作。

模板使用 `{{User.Name}}` 形式的点分 ASCII 标识符。每个名称必须有值，缺失抛出 `KeyNotFoundException`，不完整或非法 token 抛出 `FormatException`。替换只扫描模板一次，值中的 `{{other}}` 按字面保留，不递归、不执行代码。null 值替换为空字符串，名称比较遵循传入字典。普通单花括号保持不变；双花括号保留为 token 语法，需作为字面内容时可通过值传入。

`htmlEncodeValues: true` 只对值做 HTML 编码，保留宿主编写的模板标签。这适用于 HTML 正文文本位置，不提供 JavaScript、CSS 或 URL 上下文编码，也不净化模板本身。模板在内存中生成结果，宿主应限制模板和值的总大小。

SMTP 使用 [MailKit 4.18.1](https://www.nuget.org/packages/MailKit/4.18.1)，其 [MIT 许可证](https://github.com/jstedfast/MailKit/blob/master/LICENSE) 和传递依赖随 NuGet 包提供。模板仅使用 .NET 标准库，不依赖 Core、ASP.NET Core、数据库或消息中间件。

测试启动本地回环 SMTP 服务器验证真实协议、MIME 内容、收件人、拒绝、取消及接受后的清理失败。TLS 正向测试仅通过内部工厂将实际客户端锁定到本次临时证书的 SHA-256 指纹，验证 STARTTLS、连接即 TLS 和认证；公开构造器另测拒绝该自签名证书。测试不修改系统证书信任，也不向外部收件人发送消息。
