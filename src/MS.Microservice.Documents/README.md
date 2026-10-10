# 文档处理

独立文档组件，通过 `Pdf` 目录提供 PDF 页面合并、选择、拆分、文本提取、PNG 渲染与透明 PNG 叠加。公开契约使用 .NET 类型，不引用 Core、Media 或 ASP.NET Core。

PDF 图像水印可由应用调用现有 Media 组件生成透明 PNG 后传入本组件，无需重复字体或水印实现。详见 [PDF 接口与约束](docs/pdf.md)。
