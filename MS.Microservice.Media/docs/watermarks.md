# 图片水印与透明叠加图

`ImageWatermarker` 提供 `AddTextAsync` 和 `AddLogoAsync`，对方向校正后的图片添加文字或 Logo，并复用 `ImageCodec` 输出 PNG、JPEG、WebP。水印大小、输出与全部输入受同一 `ImageLimits` 约束。

```csharp
await new ImageWatermarker().AddTextAsync(input, output,
    new TextWatermark("版权所有")
    {
        FontFile = "fonts/your-font.ttf",
        FontSize = 28,
        Color = new(255, 255, 255)
    },
    new WatermarkOptions
    {
        Anchor = WatermarkAnchor.BottomRight,
        MarginX = 16,
        MarginY = 16,
        Opacity = 0.4,
        RotationDegrees = -15,
        Format = ImageOutputFormat.Png
    }, cancellationToken);
```

定位支持九个锚点。边距定义画布四周的对称内边距；定位的是旋转后的矩形边界，旋转围绕水印中心，角度支持任意有限值。非平铺水印必须完整放入边距区域，过大时明确报错。Logo 默认使用方向校正后的原始尺寸，可传入 `ImageSize` 边界框按比例适配，允许放大，不拉伸比例。

`Opacity` 范围 0–1，与文字颜色或 Logo 原有 alpha 相乘；零透明度仍验证输入，但不改变原图的解码像素。JPEG 的透明合成、质量参数及原始元数据规则与[图片处理](images.md)相同。

`Tile = true` 时，以选定锚点确定网格相位，按旋转边界宽高加 `TileGap` 铺满边距区域。边缘水印允许被裁剪，间隔不得为负。循环步长始终为正，实际网格数量超过 `MaximumTiles`（默认 10,000）时在绘制前拒绝；边界外完全不可见的网格不计入预算。

文字为一行，最多 4096 个 UTF-16 单元，须有可见内容，不允许无效 Unicode 或控制字符。可指定字体文件；未指定时仅在系统默认字体覆盖全部字符时使用默认字体，不进行隐式字体回退，不用缺字方框替代。字体不存在、缺字或塑形产生缺失字形时明确失败。字体和原始 Logo 不会被修改。

文字使用 SkiaSharp.HarfBuzz 进行单个 run 的字形塑形，支持所选字体的连字与组合字形。方向由 HarfBuzz 从该 run 推测；不执行完整 Unicode 段落双向布局、混合脚本分段、字体回退或多行排版。复杂混合双向文本应先使用有完整排版能力的组件生成透明 Logo，再通过 Logo 接口叠加。Linux 部署除 Skia native 资产外还需匹配的 HarfBuzz native 运行库，字体由宿主提供；本组件不下载或分发商业字体。

`RenderTextOverlayAsync(width, height, destination, text, placement, token)` 和 `RenderLogoOverlayAsync(width, height, logo, destination, logoSize, placement, token)` 可独立生成与目标画布同尺寸的透明 PNG，只在水印区域产生 alpha，供视频或其他合成流程复用。两者使用与完整图片相同的定位、旋转、平铺与透明度规则；不附带 JPEG 等有损输出选项。

图片及 Logo 经共享 codec 真实解码，动画和像素不完整结果遵守同一规则。输入输出从当前位置读写并保持打开，禁止同一流实例同时作为输入与输出。取消在读取、塑形、绘制与编码之间检查；单次 native 调用不可强制中断。失败或取消时输出可能有部分字节，宿主需要完整发布时应使用临时文件。
