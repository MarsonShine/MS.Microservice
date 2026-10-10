# 阿里云模板短信设计

使用入口和参数见 [README](../README.md#发送短信)。实现保留在已有 Notifications 项目中，复用官方 `AlibabaCloud.SDK.Dysmsapi20170525` 4.4.0 的请求编码、凭证和签名，不新增通知服务。

## 调用边界

`AliyunSmsSender` 构造时验证凭证和预算，创建官方凭证客户端，使用固定 `dysmsapi.aliyuncs.com`、HTTPS 和 `cn-hangzhou`，关闭 SDK 使用数据收集。仅内部测试工厂允许改用本机测试端点，公开 API 不接受请求提供的地址、协议或凭证类型。AccessKey 和带 SecurityToken 的 STS 均受支持，组件不获取或轮换凭证。

发送前完成单个号码、签名、模板和关联标识验证。号码限制为 6–20 个 ASCII 数字及可选前导加号，禁止逗号批量、空格、控制字符和 Unicode 数字。这个检查只约束请求形态，不能证明号码有效或服务商支持该国家／地区。签名和 OutId 最多 256、模板编码最多 128 个 UTF-16 单元；模板编码必须以 `SMS_` 开始，后续为 ASCII 字母或数字。

变量先复制到 Ordinal 字典，再序列化为 JSON。最多 32 项，名称最多 64、单值最多 2048 个 UTF-16 单元；严格 UTF-8 检查拒绝未配对代理项，最终按实际 JSON 字节数检查总预算。服务商收到的变量快照不受随后字典修改影响。空字典不发送 TemplateParam；组件不推导模板变量，也不将 OTP 当作数字转换。

## 超时和重试

当前依赖图为 OpenApiClient 0.1.16、TeaUtil 0.1.20 和 Tea 1.1.3。检查实际 NuGet DLL 的 `TeaCore.GetRequestMessage` 与 `DoActionAsync` 确认：Tea 把 readTimeout 与 connectTimeout 相加，使用同一个 CancellationTokenSource 控制 `HttpClient.SendAsync`，没有独立的连接和读取截止时间。该 HttpClient 还保留 100 秒默认超时。

因此组件公开一个 `TimeoutMilliseconds`，范围为 1–100,000 ms，映射为 SDK ReadTimeout 同值、ConnectTimeout 为零。零是此传输实现中的加和项，不表示允许无限请求；请求仍受总预算和 HttpClient 上限约束。公开两个独立超时会与真实传输含义不符，因此配置契约直接使用总预算。

SDK 没有接受 CancellationToken 的 SendSms 调用。组件不在外层用 WaitAsync 提前返回并遗留仍在发送的后台请求。HTTP 截止时间覆盖连接和缓冲响应，不包含本地参数验证、JSON 序列化和凭证读取；不能将其称为整个方法的严格墙钟时限。依赖升级后需重跑真实传输回归，确认映射仍成立。

RuntimeOptions 始终设置 Autoretry=false、MaxAttempts=1。组件没有额外重试循环，也没有把 OutId 解释为幂等键。超时、断连或服务端错误均可能发生在服务商实际接受之后；异常不能证明短信未发送。业务层应依据 RequestId、BizId 或服务商状态查询决定重试。

## 成功与失败

仅精确 `Code=OK` 且非空 RequestId、BizId 的响应产生接受回执。时间来自本机 UTC，在响应验证后记录，不冒充服务商投递时间。缺少正文、状态码或成功标识的响应抛出 InvalidDataException。非 OK 的业务码转换为包含 Code、RequestId 和 ProviderMessage 的 SmsRejectedException；SDK 的 HTTP、签名及传输错误直接传播。

组件不记录手机号、正文、凭证或供应商响应。宿主决定如何保护日志中的业务异常内容。当前没有回调验证、状态查询、群发、模板管理或发送去重存储；这些能力不能由接受回执替代。

## 验证

[协议测试](../../../test/MS.Microservice.Notifications.Tests/Sms/AliyunSmsSenderTests.cs)使用本机 Kestrel 和真实官方 SDK，检查中文、加号、空格和 emoji 的表单／JSON 编码、签名头存在、STS token、可选字段省略、业务拒绝、HTTP 500 单次请求及不完整成功响应。无效号码、未配对代理项、变量数量和实际 JSON 字节边界须在网络前失败。

超时回归包含 100、250、500 ms 的真实在途截止，以及同一端点先长、再短、再长预算的调用，确认 SDK 缓存 HttpClient 后仍按每次预算取消，并且不自动重发。测试等待 SDK 任务本身结束，外层测试超时只防止测试挂起。没有连接阿里云真实账户或发送收费短信；真实服务权限、模板审核、限流和最终送达仍需目标环境验收。
