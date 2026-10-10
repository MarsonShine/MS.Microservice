# 本地 OCR 设计

使用入口见 [Inference README](../src/MS.Microservice.Media.Inference/README.md)。OCR 复用现有 Inference 项目和 Python CPU 环境，不额外引入 OCR 服务或任务系统。公开结果只包含 EXIF 归一化后的图像尺寸、文字、四角坐标和识别分数。

## 模型与信任边界

采用 RapidAI 转换的 PP-OCRv5 mobile 检测和中文识别模型。发布版本为 RapidOCR `v3.9.2`，其 Git 标签指向 `095232a4c94f7f0e6600ba5bba1177010ad696d4`。本地实测文件大小和 SHA256 与该版本的[模型清单](https://github.com/RapidAI/RapidOCR/blob/095232a4c94f7f0e6600ba5bba1177010ad696d4/python/rapidocr/default_models.yaml)一致：

| 文件 | 字节 | SHA256 |
| --- | ---: | --- |
| ch_PP-OCRv5_det_mobile.onnx | 4,819,576 | 4d97c44a20d30a81aad087d6a396b08f786c4635742afc391f6621f5c6ae78ae |
| ch_PP-OCRv5_rec_mobile.onnx | 16,631,306 | 5825fc7ebf84ae7a412be049820b4d86d77620f204a041697b0494669b1742c5 |
| ppocrv5_dict.txt | 74,012 | d1979e9f794c464c0d2e0b70a7fe14dd978e9dc644c0e71f14158cdf8342af1b |

完整下载地址和许可来源保存在 [ocr-modelmanifest.json](../src/MS.Microservice.Media.Inference/worker/ocr-modelmanifest.json)。Paddle 官方[检测模型](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det)、[识别模型](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_rec)、PaddleOCR、Paddle2ONNX 和 RapidOCR 均声明 Apache-2.0；RapidAI [模型仓库的 v3.9.2 README](https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/README.md)也明确声明此许可。RapidAI 的[转换记录](https://rapidai.github.io/RapidOCRDocs/latest/en/blog/2025/05/30/rapidocr-%E9%9B%86%E6%88%90-pp-ocrv5_rec-%E6%A8%A1%E5%9E%8Bmobileserver%E8%AE%B0%E5%BD%95/)说明使用 Paddle2ONNX；转换后的文件来自 RapidAI 发布，不能表述为 Paddle 官方发布的 ONNX 文件。宿主负责保留部署包适用的许可和声明。

宿主提前安装本地模型、字典和 Python 依赖，保持这些目录及可执行文件可信且不可被请求修改。worker 对三个文件分别最多读取预期大小加一字节，检查实际长度和 SHA256，再用同一不可变字节快照创建 CPUExecutionProvider 会话或解码字典，不在校验后重新打开路径。这样，部署替换或外部进程改写路径不会改变已经校验并用于本次推理的内容；读取过程中产生不匹配内容则失败。识别模型内部字符表还必须与固定字典相同。不会联网下载、执行远程模型代码或在校验失败后改用其他识别器。

## 数据流

`ImageCodec.DecodeAsync` 从调用方当前位置读取输入，按实际字节数、原图尺寸和像素数验证，拒绝动画并统一 EXIF 方向。调用方流始终保持打开。规范化后的 PNG 写入独立的临时工作目录，之后启动 `ocr_worker.py`；Pillow 只读取此规范化 PNG，不再次应用 EXIF。透明像素先合成到不透明白底再转 BGR，避免直接丢弃 alpha 将透明黑色背景当成实黑。

输入编码预算和中间 PNG 预算独立：小体积压缩输入展开后产生更大的 PNG 是正常情况。`MaximumIntermediateBytes` 在实际文件写入时检查，超额失败并清理；它不限制 Skia 编码器先形成的原生编码缓冲区。原始光栅和透视裁剪受像素与尺寸预算约束，检测张量和识别宽度另有限额。这些约束给常规处理设定上界，不等同于操作系统内存、CPU 或磁盘配额。

`MediaProcessRunner` 复用现有无 shell 的参数传递、进程树取消、超时和标准输出/错误各 262,144 字符上限。Python 用 `-B` 启动，避免导入新增模块时写入字节码缓存。单次调用启动一个 worker 和两份 ONNX 会话；onnxruntime 保留两个已验证模型的字节快照，合计约 21.45 MB，此外还有模型会话及推理张量内存。处理器不缓存会话，也不自行限制并行请求数；同一处理器允许并发调用，每次使用独立工作目录和进程。宿主需要按内存和 CPU 容量控制并发。

## 检测与识别

检测图最长边缩小到配置上限，宽高取 32 的倍数；不足 32 的维度提升到 32。输入保持 BGR，按 `(pixel/255-0.5)/0.5` 归一化。检测概率图经过二值阈值和 2×2 膨胀，提取轮廓并按矩形内部平均概率筛选。

每个轮廓取最小面积矩形，按 `矩形面积 × 1.6 / 周长` 扩展各边。这是 DB 面积/周长距离的矩形近似，未复制 RapidOCR 的圆角多边形偏移，也不恢复弯曲文字轮廓。扩展后的角点映射回规范化原图并与图像边界相交。输出按左上、右上、右下、左下排列，透视裁剪后缩放到高 48，宽度随长宽比变化，最小填充宽度 320。超过识别宽度上限时失败，不挤压或截掉长行。

识别输出有 18,385 类，对应 CTC blank、固定字典和空格。贪心解码移除 blank 和相邻重复 token；blank 分隔的重复字及真实空格保留。分数取实际输出字符对应时间步的平均最大概率，未输出的重复 token 不参与平均。分数未做准确率校准，不能当成识别正确的概率。

检测最多允许 4,096 个轮廓；达到区域、识别宽度、输出字符或序列化预算时返回失败，避免把截断结果当成成功。字符预算按 UTF-16 代码单元计算，与 C# 字符串长度一致。最终 C# 边界再次检查图像尺寸、区域数、文字预算、分数和角点范围，并复制为只读集合。

区域按最小 y 再按最小 x 排序。这只是位置排序，不能表示多栏文档阅读顺序。当前定位和裁剪以正常方向的横排中英文印刷体为目标，可处理适度倾斜；没有方向分类、180°纠正、文档展平或版面模型。竖排、手写、曲面、复杂背景、低对比度、小字及强透视均可能漏检或误识别。空结果表示此配置未识别出文字，不能证明原图没有文字。OCR 结果不携带表格结构。

## 失败与所有权

配置错误在构造时抛出；损坏或超限图像在启动进程前失败。模型校验、推理和 worker 输出预算错误通过 `MediaProcessException` 报告，进程超时为 `TimeoutException`，调用方取消为取消异常。PNG 写入超限为 `InvalidDataException`。取消可在流读写与进程运行期间生效，不能抢占正在执行的 Skia 同步解码/编码。

每次调用只删除自己创建的随机临时目录，保留配置父目录中的其他文件。操作失败且目录清理也失败时抛出包含两者的 `AggregateException`；仅清理失败时直接传播清理异常。模型、输入流、宿主目录和 Python 可执行文件不归处理器所有。临时父目录必须可信；Unix 子目录使用仅属主权限，Windows 继承父目录 ACL。

## 验证依据

[测试代码](../test/MS.Microservice.Media.Inference.Tests/Ocr/OcrProcessorTests.cs)用实际固定模型验证中英文印刷体、8°倾斜、低对比度、透明背景、带 EXIF 的方向规范化、长行、空白图、输入/中间/输出预算、取消、超时、哈希失败、非 seekable 输入和流所有权。角点测试检查预先知道的文字位置，而不只检查坐标没有越界。[独立 Python 测试](../test/MS.Microservice.Media.Inference.Tests/Ocr/ocr_contracts.py)覆盖 CTC blank、重复字符、空格、分数计算、无效张量以及实际长度/哈希校验；真实 ONNX 会话构造前确定性替换三个临时模型/字典文件，随后仍须从已校验快照正确识别中英文，验证加载内容与校验内容相同。

这些是合成样本的功能回归，不是面向真实文档集的准确率评测。同一双语样本的额外 CPU 检查中，灰阶 240–255 仍能识别两行，250–255 和 254–255 则未检出文字，体现默认配置的低对比度漏检边界。当前验证环境为 Windows x64、Python 3.12 和 [ocr-requirements.txt](../src/MS.Microservice.Media.Inference/worker/ocr-requirements.txt) 固定版本，使用 CPUExecutionProvider；模型测试需要宿主显式配置本地 Python 和模型目录。
