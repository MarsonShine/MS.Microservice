# 加密请求怎样绑定为 MVC 模型

提交 `1009cc3` 停用了旧 AES/3DES 密文时，也删除了 Lab 的 `IApiEncrypt` 模型绑定器。旧绑定器把一次性密钥的 RSA 解密、正文的 3DES 解密和 MVC 绑定写在一起，因此不能在删除 3DES 后照原样保留。但直接删除绑定器也让“客户端发送密文，Action 收到已绑定 DTO”这项能力消失；只有新的 AES-GCM 加解密方法，并不能替代 HTTP 入口。

现在加密原语仍在 Core，MVC 绑定器位于独立的 `MS.Microservice.AspNetCore.Encryption` 组件。Core 不需要引用 MVC；通用 `MS.Microservice.AspNetCore` 也不必为未使用加密绑定的宿主引入 Core。Lab 只登记演示 DTO 并开启或关闭功能。其他 ASP.NET Core 宿主可以单独引用该组件，而不必复制 Lab 代码。

这项拆分还有一个可重复的 AOT 原因：Core 中已有 `Microsoft.System.Collection` 命名空间。若通用 AspNetCore 直接引用 Core，AOT Web 的配置绑定源码生成代码会把 `System.*` 错误解析为 `Microsoft.System.*`，原生发布出现 `CS0234`。可选组件独立后，未使用 MVC 加密绑定的原生宿主不会引用这条依赖。当前 MVC 本身不支持 Native AOT；此处保留的是通用宿主的原生发布能力，而不是宣称 MVC 绑定器可原生发布。

## 新请求格式

客户端为**每次请求**生成新的 32 字节 AES 密钥，用服务端 RSA 公钥以 OAEP-SHA256 加密密钥的 Base64 文本，再用 AES-256-GCM 加密 DTO 的 JSON。请求体仍使用原来的 `key`、`info` 两个字段，但字段值必须是新的带版本格式：

```json
{
  "key": "msenc:v1:rsa-oaep-sha256:<Base64 RSA 密文>",
  "info": "msenc:v1:aes-256-gcm:<Base64 nonce、认证标签和密文>"
}
```

服务端先要求 `key` 带 OAEP-SHA256 前缀，再解出 AES 密钥，用 AES-GCM 验证并解密 `info`，最后把明文 JSON 绑定为已登记的 DTO。旧的裸 Base64 RSA 密钥和 3DES 正文在此入口返回 `400`。Core 的 `RsaCrypt.Decrypt` 仍有面向存量数据的旧 RSA 读取路径；绑定器先检查前缀，**不会让 HTTP 请求进入该路径**。

RSA 公钥加密不证明是谁发送了请求；有公钥的调用方都能生成密文。此格式也不替代 HTTPS、认证、授权或请求幂等。服务端私钥通过 `ApiEncryptOptions:PrivateKey` 从部署配置提供，不应写进仓库；客户端必须安全取得对应公钥。当前格式没有密钥 ID，轮换密钥需要另行设计版本与过渡期。

## 哪些参数会解密

宿主调用 `AddApiDecryptModelBinding` 时显式登记实现 `IApiEncrypt` 的 DTO 和它的 `JsonTypeInfo<T>`：

```csharp
builder.Services.AddControllers()
    .AddApiDecryptModelBinding(builder.Configuration,
        static models => models.Add(AppJsonContext.Default.CreateRequest));
```

`ApiEncryptOptions:IsEnabled` 默认关闭。关闭时，MVC 按原方式读取普通 JSON；开启时，只有**登记过的模型类型**走解密绑定。即使开启，其他模型仍由 MVC 原有绑定器处理。Action 标注 `[NoEncrypt]` 时，已登记 DTO 也按普通 JSON 绑定。Lab 的 `/api/lab/encryption/echo` 和 `/api/lab/encryption/plain` 分别演示加密与明文例外。

旧的 `IApiEncrypt`、`NoEncryptAttribute` 位于 Lab 命名空间；重新接入时需引用 `MS.Microservice.AspNetCore.Encryption`。字段名 `key`、`info` 可以沿用，但旧字段值不能与新绑定器混用。

| 条件 | 请求 | 结果 |
| --- | --- | --- |
| 功能关闭 | 普通 JSON 发给 `echo` | 原 MVC 绑定，返回 `200`。 |
| 功能开启 | 新格式发给 `echo` | 解密为 DTO，返回 `200`。 |
| 功能开启 | 普通 JSON 发给 `echo` | 绑定失败，返回 `400`，Action 不执行。 |
| 功能开启 | 普通 JSON 发给 `[NoEncrypt]` 的 `plain` | 返回 `200`。 |
| 功能开启 | 普通 JSON 发给未登记模型的接口 | 原 MVC 绑定流程。 |
| 功能开启 | 旧格式、缺字段、错误密钥或被改动的密文 | 返回 `400`，不把密钥或密文错误返回给客户端。 |

上表的“未登记模型”指没有实现 `IApiEncrypt` 的普通 DTO。如果 DTO 已实现该接口却忘记登记 `JsonTypeInfo<T>`，绑定器提供者会报配置错误，不会静默交给 MVC 接受明文。

模型绑定器提供者按登记时的**准确类型**选择绑定器，不扫描程序集，也不调用按运行时 `Type` 反序列化的 JSON API。为了拒绝漏登记的 `IApiEncrypt` 模型，提供者在选择绑定器时对模型 `Type` 做一次接口兼容性检查；这是本次唯一新增的运行时类型检查，不在每个请求中反序列化时扫描成员。解密后的 JSON 使用调用方明确给出的源码生成元数据。MVC 本身目前不支持 Native AOT；这里控制新增反射的范围，并不表示整个 MVC 宿主能够原生发布。

启用时缺少或配置了无效的 RSA 私钥，宿主在注册阶段失败。请求取消会传给 JSON 读取；格式错误、认证标签失败和无法解析的模型统一成为模型状态错误，由 `[ApiController]` 返回 `400`。测试用真实 TestServer 请求覆盖了上表中的行为。这里恢复的是功能契约，没有加解密前后的性能基线，也不声称吞吐改善。后续如果实际需要密钥轮换，应在请求格式中加入密钥 ID，并在选定密钥管理设施后单独设计。
