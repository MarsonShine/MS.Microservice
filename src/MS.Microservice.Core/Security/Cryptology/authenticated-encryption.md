# 对称加密格式

`CryptologyHelper.AesCrypt` 以前接受一个字符串密钥，默认把它转换成 16 字节 AES 密钥。转换函数只在密钥的 UTF-8 字节数少于 16 时复制内容。密钥达到 16 字节后，函数直接返回新建的全零数组。因此，默认调用中两个不同的长密钥实际使用同一个全零密钥；旧测试只验证“加密后能解密”，没有发现这个错误。

旧实现还使用 AES-ECB。相同的明文块会得到相同的密文块，密文也没有认证标签。`DesCrypt` 使用固定 IV `12345678` 做 3DES-CBC，同一密钥下重复加密会重复使用这个 IV，也没有校验密文是否被改动。把 `autoHandle` 设为 `false` 虽能绕过全零密钥错误，仍不能解决 ECB 和缺少认证的问题。

## 当前格式

现在的 API 要求调用方传入**恰好 32 字节的密钥**，使用 .NET 的 `AesGcm` 加密 UTF-8 文本。每次调用由 `RandomNumberGenerator` 生成新的 12 字节 nonce，认证标签固定为 16 字节。密文是下面的字符串：

```text
msenc:v1:aes-256-gcm:<Base64(nonce[12] || tag[16] || ciphertext)>
```

前缀同时作为 GCM 的关联数据参与认证。解密先检查格式与长度，再验证认证标签；密钥错误，或 nonce、标签、正文有改动时，都不能得到明文。未知版本直接失败，不尝试按旧格式解密。这个实现只调用 .NET 的静态类型和加密 API，没有反射或动态代码生成。

应用必须生成一次随机密钥并安全保存，而不是在每次请求时生成新密钥。例如，初始化密钥时可用 `RandomNumberGenerator.GetBytes(32)`，把生成值的 Base64 表示保存到应用的密钥管理设施。使用时先解码，传入原始 32 字节：

```csharp
byte[] key = Convert.FromBase64String(configuredKey);
string encrypted = CryptologyHelper.AesCrypt.Encrypt(key, "需要保护的文本");
string plaintext = CryptologyHelper.AesCrypt.Decrypt(key, encrypted);
```

密钥不能用短口令补零，也不能写在仓库配置中。密钥丢失后，现有密文无法恢复。格式中的 `v1` 表示密文协议版本，不表示密钥版本；目前没有内置密钥轮换或密钥 ID。

## 旧调用方的变化

原先的 `AesCrypt.Encrypt(string key, string content, bool autoHandle)` 与对应解密重载已删除。新方法接受 `ReadOnlySpan<byte>` 密钥，旧的字符串密钥调用会在编译期报错。`DesCrypt` 已删除。两种旧密文都没有本库提供的解密或迁移入口；部署前如有持久化旧密文，需要在旧版本环境中自行处理。旧 RSA + 3DES 请求绑定器已停用；替代的可选 [MVC 加密模型绑定](../../../MS.Microservice.AspNetCore.Encryption/docs/encrypted-model-binding.md) 只接受 RSA-OAEP + AES-GCM 新格式。

RSA 和 HMAC 辅助方法不属于本次密文格式修改。Lab 的旧密码验证仍使用 HMAC，以便用户登录后升级已有密码哈希；它没有被改造成通用加密 API。

## 验证范围与后续

测试覆盖空文本、中文和长文本、相同输入的随机 nonce、错误密钥、篡改 nonce/标签/正文、旧 Base64 密文、未知版本、截断和非法输入。此次是安全修复，没有把加解密耗时作为优化目标，也没有可对比的性能基线，因此不声称吞吐量提升。

将来如需轮换密钥，可在封套中增加密钥 ID，并在解密时按 ID 选择仍在有效期内的密钥。届时需要新的格式版本和独立测试；当前 `v1` 不包含这一能力。

格式参数依据 .NET 的 [跨平台加密说明](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography)；[AesGcm 文档](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt?view=net-10.0)说明同一密钥不能重复使用 nonce。
