# 视频水印的画布与时间范围

使用入口见 [Media README](../README.md)。视频水印复用 `ImageWatermarker` 产生完整画布的透明 PNG，字体、字形、定位、旋转、平铺与 alpha 只在[图片水印层](watermarks.md)定义一套规则。

## 画布与过滤图

先探测方向校正后的像素栅格，再校验画布和偶数补齐后的尺寸。PNG 与画布同尺寸，FFmpeg 以 `overlay=0:0` 合成；非方形像素仍按源栅格定位，不能把 SAR 显示尺寸当作该 PNG 的像素宽高。

时间启用表达式为 `gte(t, Start) * lt(t, End)`，没有 End 时只检查下界。半开区间避免边界帧重复延长，超出源末尾的 End 不创建新帧。过滤图不使用 copyts，时间基于 FFmpeg 默认归一化输入；passthrough 不强制固定帧率，不承诺原容器绝对时间戳。

overlay 后按 YUV420 补齐偶数边长，删除已应用的 DISPLAYMATRIX 并清空 rotate tag。只设置 tag 为零不足以防止复杂图传播旧矩阵；该处理与编辑、拼接保持一致。

主视频取第一条非封面轨，映射所有音轨，无音轨时仍无音轨。输出重新编码为 H.264/AAC 或 VP9/Opus，不能按声音存在性检查推断样本、编码延迟或颜色无损。字幕、数据和附件不映射；高位深／HDR 不提供色调映射保证。

## 两个暂存资源

视频与完整画布 PNG 均在目标目录创建，分别记录本次创建所有权。PNG 必须在编码和结果校验后成功清理，才允许统一发布方法移动视频；PNG 清理失败时不能把已写好的视频视为成功发布。

处理与 PNG 清理同时失败保留两者。进程取消／超时先终止进程树，外层再处理文件；已有输入和目标始终不被本次清理删除。每调用的图像内存随画布增长，预算不能替代并发控制。

Logo 流从当前位置读并保持打开。字体错误或 Logo 解码失败在 PNG 阶段传播，不回退成空水印；输出已存在时在启动前拒绝。

## 回归

[VideoWatermarkTests](../test/MS.Microservice.Media.Tests/Video/VideoWatermarkTests.cs) 覆盖三种容器、区间边界逐帧检查、文字／Logo、透明度、声音存在性、三种非零直角元数据、预算、错误编码器、取消及既有目标保护。旋转 fixture 使用 FFmpeg 6 及以上的 `-display_rotation`。

真实工具的开启方式见[视频进程说明](video.md)。过滤语义以 [FFmpeg overlay 文档](https://www.ffmpeg.org/ffmpeg-filters.html#overlay)为准，升级后应重新检查帧边界与矩阵传播。
