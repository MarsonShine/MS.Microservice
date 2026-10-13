# HTML 渲染设计

渲染由官方 .NET Playwright SDK 1.63.0 驱动已安装的 Chromium 浏览器。公共 API 与 worker 入口位于同一个可选可执行程序集，普通 Documents 不依赖此组件。API 和参数见 [README](../README.md)。Playwright 的 .NET 封装按 MIT 发布，随包的 playwright-core 按 Apache-2.0 发布，Node 等分发文件另有随附许可；保留部署文件中的许可。

## 生命周期与协议

公共入口验证 HTML、纸张和资源预算，复制资源字节，避免调用期间数组修改影响结果。随后启动自己的 `dotnet exec` worker，以标准输入发送 JSON；HTML 和资源不进入命令行。标准输出为四字节小端页数及验证后的 PDF，标准错误只保留前 16 KiB，同时继续排空。

worker 在完整读取请求后才创建 Playwright。父进程在发送请求前将其绑定到私有 Windows Job Object，因此 Node、浏览器和子进程继承同一所有权边界；不启用 breakaway。只给该子进程设置 `TEMP`、`TMP`、`TMPDIR`，将默认浏览器 profile 和其他临时文件放入本次目录。清除 Playwright 驱动路径覆盖、调试及 Node 注入环境变量，确保使用部署中的驱动，不影响宿主环境。

stdout、stderr、stdin 和进程等待并行执行。任一管道失败会唤醒监督逻辑，不等另一条堵塞管道自然完成。处理期限覆盖 worker 启动、渲染、PDF 检查和 SDK 关闭；取消／期限到达后终止自己的 Job，等待根进程退出及 Job 的 `ActiveProcesses = 0`，观察所有管道任务，最后清理目录。收尾共享独立的十秒预算，目标流复制在其后进行。

SDK 的 `Browser.CloseAsync` 没有取消参数，`IPlaywright.Dispose` 会关闭 stdin 后同步等待 Node 驱动退出。只在外层调用 `WaitAsync` 无法终止这些操作，所以 SDK 生命周期全部放在可终止的 worker 内。正常路径先关闭 context、browser、driver，再输出成功数据。处理异常与关闭异常聚合，不把清理失败覆盖为原错误；对应行为可核对 [固定版本 SDK 传输源码](https://github.com/microsoft/playwright-dotnet/blob/v1.63.0/src/Playwright/Transport/StdIOTransport.cs)。

Windows Job 为本片明确的支持边界，其子进程继承和嵌套规则见 [Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)。构造器在其他系统抛出 `PlatformNotSupportedException`，内部 CLI 返回 69；误调用程序且参数不精确等于 `--html-worker` 时返回 64。worker 的预算／数据错误返回 3，其他错误返回 1。没有实现或声称其他系统具有等价进程回收保证。

宿主额外施加的 Windows 进程沙箱可能使浏览器启动后在创建上下文或页面时阻塞，直至处理期限。浏览器启动成功不足以证明环境可用；排查时应以相同配置和宿主正常运行权限核对，保留 Chromium 沙箱及原有处理期限。

## 页面与资源策略

每次创建全新浏览器和 context，显式设置 `ChromiumSandbox = true`、禁用 JavaScript、阻止 Service Worker、拒绝下载及 HTTPS 错误绕过，并设置 offline。主页面使用随机路径的虚拟 HTTPS 地址，由路由直接提供 HTML；没有文件 URL 或调用方可指定的 base URL。

HTTP CSP 头包含 sandbox、默认拒绝、脚本／框架／对象／连接／媒体拒绝，仅对本次路径允许图片、字体和 CSS，内联 CSS 可用。`base-uri 'none'` 阻止文档覆盖基址。Context 路由只放行一次主导航和精确匹配的非导航资源，其余请求中止。浏览器额外禁用主机解析，所有资源由本次有界字典提供。

路由并不是所有资源的隔离机制：`data:` 不经过网络路由，`srcdoc` 也不被 `frame-src 'none'` 阻止。初次加载后，组件通过 Playwright 的 frame 树将每个直接子 frame 导航到 `about:blank`；这会清除嵌套 frame，并覆盖关闭的 shadow root 内的 frame，不依赖仅能遍历普通 DOM 的选择器。框架元素的 CSS 盒保留，因此可能增加空页。禁止的资源可能被浏览器／CSP 提前阻止，不能用路由回调数量代表全部拦截情况。

图片资源先在 worker 内用 Skia codec 读取实际类型、尺寸和帧数，验证声明 MIME 对应 PNG／JPEG／WebP，像素数量有界且拒绝多帧。Skia 静态图片的帧数可能是零，动画判断只用 `FrameCount > 1`。字体交给浏览器加载，缺失字形或无效字体可能回退到系统字体；不保证所有 Unicode 字形或不同宿主的相同分页。

## PDF 与内存预算

`Page.PdfAsync` 会一次返回完整字节数组，不能在接收前施加输出大小限制。本组件使用公开 CDP 接口 `Page.printToPDF(ReturnAsStream)` 与 `IO.read`，按剩余额度加一个探测字节分块读取，并在扩容前检查字节预算。流在成功和失败路径均关闭。

打印范围最多包含 `MaximumPages + 1` 页，再用现有 `PdfProcessor.InspectAsync` 验证有效性、页数与实际尺寸。多出一页会拒绝整个结果，不静默截断。纸张尺寸最大 1000 mm；CSS 尺寸启用时同样检查实际 PDF。PDF 检查在 worker 中完成，父进程只检查有界协议及成功退出状态，不在超时边界外进行原生解析。

源字符串、资源、JSON、DOM、PDF 和浏览器内部结构可能同时存在，预算限制各类数据，不能当作进程 RAM 硬配额。大文件和并发应由宿主另行限制。输出先收在自有缓冲，worker 退出与目录清理完成后才写目标；最终写入的取消和 I/O 错误可能留下部分内容。

## 清理异常与发布

`Process.WaitForExitAsync` 只确认根进程；这是不能单独作为目录删除前提的原因。Job 归零后，仍实测出现过短暂共享锁，而紧接着的删除成功。句柄持有者未证实，可能涉及内核收尾或外部扫描；处理只依据 Windows 的共享／锁冲突兼容规则。

自有目录的绝对路径须位于可信临时根内。只对 Win32 32／33 的共享／锁冲突在剩余清理预算内等待重试；权限和其他错误立即返回。失败时聚合原处理异常与清理异常并保留目录路径。组件不杀其他进程，也不按某个浏览器数据库文件名特殊处理。

压力验证中也曾观察到错误 5，事后文件非只读且能够取得删除权限，但未证实失败瞬间的持有者或权限状态。没有据此将所有 AccessDenied 当作短暂锁重试；该路径仍明确失败并保留目录。并行 CPU 密集任务也可能使普通页面达到处理期限，期限到达仍执行同一终止与清理规则，不保证每个页面都能在默认时间内完成。

## 部署元数据

目录部署必须包含 Rendering DLL、它自己的 runtimeconfig／deps，以及 SDK 的 `.playwright` 文件。ProjectReference 通过 `None` 项目复制运行元数据；NuGet 将元数据显式放在 `tools/net10.0`，再由 `buildTransitive` 复制到消费者目录。SDK 自己的 targets 负责驱动复制。

启动受监督 worker 时，入口仍为 Rendering DLL，但 `dotnet exec` 使用消费者的 runtimeconfig／deps。消费者可能升级传递依赖，或由 ASP.NET Core 共享框架提供 DLL 而不在输出目录复制；重用库打包时的独立 console 元数据会使这些已解析依赖在 worker 中缺失。`APP_CONTEXT_DEPS_FILES` 的第一项是应用依赖清单，去掉完整 `.deps.json` 后缀可取得标准同名 runtimeconfig；缺失时明确拒绝，不回退选取框架清单或库自己的旧依赖图。VSTest 的入口是 `testhost`，实际应用清单却属于测试项目，因此不能通过入口程序集名猜测配置。标准 Console、ASP.NET Core、项目引用、NuGet 及目录发布均沿用同一规则；自定义原生宿主或不提供匹配配置的动态插件加载不在支持范围。运行时清单与探测规则见 [.NET 官方文档](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/default-probing)。

复制项要幂等，不能以 `Content Pack=false` 同时承担显式包文件：NuGet 会将该绝对文件标记为全局排除，导致自定义 tools 路径也不入包。本组件使用 `None Pack=false` 处理复制，独立打包项处理 tools。单文件、裁剪与 Native AOT 无法保证这些部署和 SDK 条件，明确不支持。

验证包括实际 Windows Edge、中文／emoji 文本提取、PDFium 栅格和 PNG／JPEG／WebP 像素、CSS／本地字体资源、分页／页尺寸、编码和页数预算、重复调用及调用方前缀。安全矩阵覆盖 HTTP、file、data、srcdoc、关闭 shadow root、脚本、刷新、基址和预连接，独立本地监听器验证零连接。取消／超时覆盖不同启动阶段，清理测试覆盖真实共享锁、权限失败、原异常保留、目录边界及部分目标写入；另以独立消费者验证 NuGet 和 publish，不能只凭项目内测试推断部署正确。
