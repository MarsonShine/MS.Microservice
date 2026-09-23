# RSA 密文格式与旧数据读取

`RsaCrypt` 原来用 PKCS#1 v1.5 填充加密，结果是没有版本标记的 Base64 文本。它只能加密一块，解密却允许把多个 RSA 块直接拼接，再一起解码为字符串。只要继续调用旧的 `Encrypt`，新数据就会不断采用旧格式；解密端也无法从文本本身判断算法版本。.NET 把 PKCS#1 v1.5 加密定位为兼容旧应用的方式，建议新应用使用 OAEP。

## 新数据怎样写入

`Encrypt` 现在使用 OAEP-SHA256，且只接受至少 2048 位的 RSA 公钥。公钥仍是 Base64 编码的 DER `SubjectPublicKeyInfo`。生成的文本格式为：

```text
msenc:v1:rsa-oaep-sha256:<Base64(一个 RSA 密文块)>
```

前缀用于选择算法，不能去掉。2048 位密钥的输出块为 256 字节。OAEP-SHA256 为一块消息保留 `2 × 32 + 2` 字节，所以一次最多加密 `256 - 66 = 190` 字节明文。限制按编码后的**字节数**计算：63 个 UTF-8 汉字是 189 字节，可以放入；64 个是 192 字节，会被拒绝。较长的正文应使用对称加密协议；不要把正文切成多个独立的 RSA 块。

```csharp
string ciphertext = CryptologyHelper.RsaCrypt.Encrypt(text, publicKeyBase64, Encoding.UTF8);
string plaintext = CryptologyHelper.RsaCrypt.Decrypt(ciphertext, privateKeyBase64, Encoding.UTF8);
```

私钥仍是 Base64 编码的 DER PKCS#8。导入公钥和私钥时要求 DER 完整消费，尾随数据会被拒绝。新格式密文必须恰好是该私钥模长的一块；空密文、截断、额外块、错误密钥和被修改的密文都会失败。OAEP 保护密文不被静默篡改，但持有公钥的任何人都能生成密文；它不证明发送者身份。

## 旧数据怎样读取

`Decrypt` 看见上面的前缀时只用 OAEP-SHA256，不会在验证失败后尝试 PKCS#1 v1.5。看见其他 `msenc:` 前缀时直接拒绝。只有**没有前缀**的旧 Base64 数据才按 PKCS#1 v1.5 解密；该分支仍接受旧的 1024 位密钥及多个完整 RSA 块，以便读取已有数据。空的旧 Base64 字符串现在会失败，避免把无密文误认为空明文。

这意味着本库仍能读取旧 RSA 密文，但新 `Encrypt` 的输出不再适用于只认识旧裸 Base64 格式的外部解密器。升级前需要核对这些外部调用方。本次没有删除旧 RSA 读取能力，因为停用旧 AES/3DES 的决定不等于可以丢弃 RSA 存量数据。

旧分支仍使用 PKCS#1 v1.5，不能把来自不受信任输入的解密错误当成可对外区分的响应，否则可能形成填充判断通道。应用应控制旧数据入口并统一失败响应。待确认存量密文和外部使用者都已迁移，再单独删除旧分支；现在的格式没有自动迁移旧密文的功能。

实现只使用 .NET 的 `RSA` 和静态密钥导入 API，没有反射或动态代码生成。此次是安全格式调整，不以性能为目标，也没有用未经测量的数字声称性能收益。

参考 [.NET RSA 填充模式说明](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsaencryptionpaddingmode?view=net-10.0)及 [RSAEncryptionPadding.OaepSHA256](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsaencryptionpadding.oaepsha256?view=net-10.0)。
