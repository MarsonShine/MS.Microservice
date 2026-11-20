# DOCX 排版与转换设计

DOCX 的分页依赖排版引擎。Open XML SDK 用于检查包结构，LibreOffice Writer 执行排版和 PDF 导出，现有 `PdfProcessor` 检查生成物。转换与 HTML 渲染共用可选 Rendering 项目及进程监督层，普通 Documents 使用者不必依赖外部引擎。

## 输入检查与支持域

父进程从当前流位置读取有界快照，最多多读一字节判断超限，不依赖输入可 seek。随后将完整请求发送到门控 worker。ZIP 展开、XML、Open XML schema 和图片检查均在 worker 内执行，受处理期限约束。

`WordPackage.CheckAsync` 与模板填充共用 ZIP 条目、实际展开字节和 XML 字符预算，禁止 DTD、XML 外部解析、重复条目和 Strict WordprocessingML。共享的内部预算不包含模板替换数量，转换不调用 `FillAsync`，也不保存或修复原包。Documents 仅向 Rendering 开放必要的内部访问。

检查遍历包级关系及所有部件，分别覆盖 `ExternalRelationships` 和不包含在其中的外部 `HyperlinkRelationships`。宏、签名、保护、OLE、嵌入包、媒体、altChunk、VML 均在支持域之外。VML 的 `src` 属性和 SVG 内部资源不一定经过 OPC 关系，因此同时拒绝 VML 标记和非 PNG／JPEG 图片部件，不能仅依赖关系检查。

PNG／JPEG 会检查实际编码格式与 MIME、尺寸及动画帧数。Skia 的静态帧数可以是 0 或 1，只有大于 1 才视为动画。图片部件的 `ZipWrappingStream` 不可 seek，不能直接交给要求 rewind 的解码器：先用已有有界读取逻辑取得编码字节，再交给共享图片检查。该检查读取文件头，不承担完整图像容器或像素真实性验证；实际排版仍由 LibreOffice 完成。

每个图片快照受实际 ZIP 展开预算约束。输入、JSON 传输、DOCX DOM、图片编码副本和原生解码器均有额外内存成本；这些预算不等于进程内存硬配额。

## 字段识别

简单字段读取完整 `w:instr` 属性。复杂字段按部件内的文档顺序维护 begin／separate／end 栈，拼接跨 run 的 `instrText`。指令阶段只允许指令文本、run 格式和字段结构，拒绝普通文字、控制字符及嵌套字段；缓存结果阶段可包含普通文字及支持的嵌套字段。这样可避免引擎将普通 `w:t` 等内容也作为命令，而检查只拼接 `instrText` 的语义差异。未闭合字段、孤立指令、重复分隔符等结构错误会失败，字段不能跨部件配对。

完整指令只允许 `PAGE`、`NUMPAGES`、`SECTION`、`SECTIONPAGES`，其后可重复使用 `\* Arabic`、`\* ROMAN`、`\* ALPHABETIC`、`\* MERGEFORMAT` 或 `\* CHARFORMAT` 通用格式开关。识别不区分大小写，原始指令不改写。指令上限为 1024 个 UTF-16 字符，嵌套上限为 64；未知字段及额外参数均拒绝，不用关键字子串判断是否安全。

页码字段由引擎随排版计算。日期、目录、邮件合并、DDE、外部文本和图片字段不在当前支持域；保留缓存值的转换行为不等于支持其动态语义。

## 原生引擎与进程所有权

父进程启动本程序集的 `--word-worker`，先加入设置了 `KILL_ON_JOB_CLOSE` 的 Windows Job，再发送完整 stdin 请求。worker 只有读到请求 EOF 后才会启动 `soffice.com`，因此 LibreOffice launcher、`soffice.bin`、重启后的子进程及 PDF 校验都归该 job 所有。监督层并行排空输入、输出和有界诊断；任一路失败、取消或超时都会停止自有进程树并等待 job 归零。

每次使用全新 `UserInstallation` URI、输入文件、输出目录和私有 TEMP 环境，不接管已有 Office 实例，也不读取用户 Office 配置。profile 禁用宏、提高宏安全级别，关闭 Writer 链接、字段和图表更新；它不是操作系统沙箱。真实 `file:` 图片外链在上述 profile 下仍能读取文件，因此必须在结构检查层拒绝。

引擎子进程会移除 `CRASH_DUMP_ENABLE`，并设置 `-env:CrashDumpEnable=false`。在已验证版本中，崩溃记录初始化会查询系统代理并可能进入反向 DNS，导致完全本地的文档转换等待。此配置只关闭本次 headless 引擎的崩溃记录，不修改安装文件、系统代理、DNS 或 hosts。

该行为可从 [固定版本的 CrashReporter](https://github.com/LibreOffice/core/blob/bf6ef2662401c7d6dce671ab4deb26ee0af3a7c1/desktop/source/app/crashreport.cxx) 与 [代理解析](https://github.com/LibreOffice/core/blob/bf6ef2662401c7d6dce671ab4deb26ee0af3a7c1/ucbhelper/source/client/proxydecider.cxx) 核对。启动参数和 PDF 属性分别采用官方 [命令行参数](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html) 与 [PDF 导出参数](https://help.libreoffice.org/latest/en-US/text/shared/guide/pdf_params.html)。

## 输出与失败语义

PDF 导出范围是 `1..MaximumPages+1`，并关闭自动空页跳过。多取一页仅用于检测超限：实际 PDF 页数超过配置就失败，不发布截断结果。该限制不能阻止引擎排版完整源文档，也不是原生磁盘或内存配额；原生 PDF 先写私有目录，读取时再执行编码字节上限。

worker 等待引擎退出，再有界读取 PDF 并用 `PdfProcessor.InspectAsync` 校验页数和结构。只有完成校验才发送四字节页数头及 PDF；父进程完成 job 回收和目录清理后才复制到调用方流。

默认 60 秒期限覆盖 worker 启动、包检查、原生转换、PDF 检查和结果传输。收尾另有最多 10 秒，继承共享清理规则：只对 Windows 共享／锁冲突 32、33 有界重试；权限等其他失败保留异常和目录位置。主错误与清理错误同时存在时聚合返回。输入读取及最终调用方流写入的时长不包含在 worker 期限内。

验证和转换失败时目标保持原状；最后一次目标写入仍可能部分成功，复制错误或取消不会回滚调用方流。调用方负责底层文件不与源文件重合、覆盖策略、原子发布及并发资源限制。

## 验证

`WordRenderPolicyTests` 使用真实 Open XML 包覆盖正文、页眉、页脚、包级外部关系、拆分与嵌套字段、VML／SVG、ZIP／XML／图片预算和不可 seek 的图片部件。`WordPdfConverterTests` 使用真实 LibreOffice、PDF 文本提取和 PDFium 栅格，检查中文、emoji、表格、分页、页码更新、横向纸张、JPEG／PNG、超限、运行中取消、超时、流所有权及部分目标写入。

原生测试通过 `LIBREOFFICE_TEST_PATH` 指定可信引擎。HTML 和 DOCX 原生测试属于同一 xUnit collection，避免同时启动排版引擎干扰时限。共享 ZIP／XML helper 变动须回归现有 Documents 测试；监督层或图片检查变动须回归 HTML 渲染测试。源码引用、独立 NuGet 和目录发布均需验证 worker 元数据和真实转换，不能只验证包中有一个 DLL。
