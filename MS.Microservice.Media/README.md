# 媒体处理组件

可选的 .NET 10 媒体组件，使用者按需引用 `MS.Microservice.Media`。基础图像处理使用 SkiaSharp，未引用 Core、AI、数据库、消息或示例宿主。

| 能力 | 接入与边界 |
| --- | --- |
| 静态图片 | [图片处理](docs/images.md)：EXIF 方向、裁剪、直角旋转、翻转、三种缩放模式、PNG/JPEG/WebP 输出 |

公共 API 不暴露 Skia 类型。宿主负责输入权限、上传和保留策略、并发资源预算、结果发布；图像组件不关闭调用方流。原生 Skia 运行时需适配部署平台，本项目包含常用桌面运行时及 Linux no-dependencies 资产，实际部署仍需在目标系统验证。

独立开发可打开 `MS.Microservice.Media.slnx`，运行模块测试：

```powershell
dotnet test MS.Microservice.Media.slnx -c Release
```

源码与 NuGet 接入沿用仓库的[组件消费流程](../docs/components/consumption.md)。
