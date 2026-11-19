# Azure Lab 实测

基线为 `1cb983a`。2026-11-19 使用 .NET SDK 11.0.100 验证了更新后的配置文档和独立 Azure Lab。范围包括两套 Chat Responses 部署、East US Speech 合成和 Azure 文生图。

在仓库根目录运行 `dotnet run --project samples/Lab/MS.Microservice.Lab.Azure -c Release -- <Downloads>`。实验读取环境变量中的密钥，进程退出码为 0。六次调用均返回 HTTP 200。结果目录为 `Downloads/Azure-Lab-20261119-102453Z-9c9fd1c0`。

| 操作 | 可复查结果 |
| --- | --- |
| `gpt-6.1-sol` | 两轮各 2,668 输入 token。第一轮写入 2,665 个缓存 token，第二轮读取 2,665 个。 |
| `gpt-6.1-terra` | 两轮各 2,668 输入 token。第一轮写入 2,665 个缓存 token，第二轮读取 2,665 个。 |
| East US Speech | `azure-speech-eastus.mp3` 经 ffprobe 识别为 24 kHz 单声道 MP3，时长 5.352 秒。 |
| `gpt-image-2.5-flare` | `gpt-image-2.5-flare.png` 经 Pillow 验证为 1024 × 1024 PNG。Azure 报告 46 输入 token 和 196 输出 token。 |

`cache-tokens.csv` 和 `summary.json` 保存实测用量。对文本工件检查了本次使用的密钥值，各文件均未匹配到密钥。指南中的完整 JSON 与 [Lab appsettings.json](../../../samples/Lab/MS.Microservice.Lab.Azure/appsettings.json) 语义一致。

`dotnet test MS.Microservice.AI/MS.Microservice.AI.slnx -c Release` 通过 241 项测试，其中 Azure 57 项。Lab Release 构建通过，0 警告、0 错误。NuGet 包中的[接入指南](../../../MS.Microservice.AI/docs/azure.md)和[参数参考](../../../MS.Microservice.AI/docs/azure-configuration.md)与源文件一致。
