# 通知组件

提供 SMTP 邮件、命名模板和本地持久化站内通知。引用 `MS.Microservice.Notifications` 项目或包即可使用，无需启动单独服务；[源码与包接入方式](../../docs/components/consumption.md)。

| 功能 | 入口 | 用途 |
| --- | --- | --- |
| 邮件 | `SmtpEmailSender.SendAsync` | 发送纯文本或 HTML 邮件，支持 To、Cc、Bcc |
| 模板 | `NamedTokenTemplate.Render` | 将 `{{Name}}` 或 `{{User.Name}}` 替换为字典值 |
| 站内通知 | `LocalNotificationInbox` | 按用户发布、分页查询、统计未读数、标记已读 |

## 发送邮件

```csharp
using MS.Microservice.Notifications.Email;
using MS.Microservice.Notifications.Templates;

var sender = new SmtpEmailSender(new SmtpEmailOptions
{
    Host = "smtp.example.com",
    UserName = configuredUserName,
    Password = configuredPassword
});
var body = NamedTokenTemplate.Render("你好，{{Name}}。", new Dictionary<string, string?>
{
    ["Name"] = "张三"
});
var accepted = await sender.SendAsync(new EmailMessage
{
    From = new("sender@example.com", "应用通知"),
    To = [new("recipient@example.com")],
    Subject = "处理完成",
    TextBody = body
}, cancellationToken);
```

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `Host` | 必填 | SMTP 服务器 |
| `Port` | `587` | SMTP 端口 |
| `TlsMode` | `RequireStartTls` | 要求 STARTTLS；也可选择连接即 TLS |
| `UserName` / `Password` | 不认证 | 使用时须成对提供，且必须启用 TLS |
| `TimeoutMilliseconds` | `30000` | 客户端 I/O 等待超时 |
| `DisconnectTimeoutMilliseconds` | `2000` | 发送成功后的断开等待上限 |

`EmailMessage` 至少需要一个收件人及 `TextBody`、`HtmlBody` 中的一种正文。显示名单独传入 `EmailAddress.DisplayName`。当前接口不提供附件或 OAuth token 获取。

返回回执只表示 SMTP 服务器已接受，最终送达需邮件服务的投递反馈确认。组件不自动重试；超时或网络中断时，远端是否已接受可能无法确定，应先核对发送状态再决定重发。

模板缺少变量会报错，不执行模板中的代码。生成 HTML 文本时可设置 `htmlEncodeValues: true`；它只编码替换值，不能用于 JavaScript、CSS 或 URL 上下文，也不净化模板本身。

## 站内通知

```csharp
using MS.Microservice.Notifications.Inbox;

var inbox = new LocalNotificationInbox(configuredPrivateDirectory);
var message = await inbox.PublishAsync(userId, "文件处理完成", "输出已生成。",
    "/tools/jobs/123", cancellationToken);
var page = await inbox.ListAsync(userId, take: 20, cancellationToken: cancellationToken);
await inbox.MarkReadAsync(userId, message.Id, cancellationToken);
```

| 参数或结果 | 默认值 | 说明 |
| --- | --- | --- |
| `owner` | 必填 | 由宿主从已验证身份取得，所有操作都须检查所属用户权限 |
| `subject` / `body` | 必填 | 纯文本；标题最多 200、正文最多 16,000 个 UTF-16 单元 |
| `actionPath` | 无 | 应用内以 `/` 开始的路径，最多 2048 个 UTF-16 单元 |
| `skip` / `take` | `0` / `50` | 按创建时间和 Id 倒序分页，默认每页最多 100 条 |
| `MaximumNotificationBytes` | `64 KiB` | 单份通知或已读回执的编码大小上限 |
| `MaximumScanEntries` / `MaximumScanBytes` | `10000` / `32 MiB` | 一次查询的通知数和累计读取上限 |
| `TotalCount` / `UnreadCount` | — | 本次查询扫描到的总数和未读数 |

通知和已读时间保存在指定目录，重启后可继续读取。目录需由应用可信管理，内容不加密；宿主负责历史保留期。查询超出扫描预算会报错，持续大量通知应使用数据库后端。并发插入时 offset 翻页可能重复或跳项，可按 Id 去重或重新加载。

## 实现说明

- [SMTP、模板与异常处理](docs/email-and-templates-design.md)
- [站内通知的持久化与并发规则](docs/local-inbox-design.md)
