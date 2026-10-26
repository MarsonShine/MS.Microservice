# Slice 07：Core HTTP 辅助契约收敛

Status: complete

范围：Core/Http/HttpClientExtensions.cs、Core/Net/Http/LogHttpClient.cs与QueryStringParameters.cs，必要共享读取代码；Core HTTP/JSON测试与README；状态/证据。范围外：重试、AI、全局HTTP预算、序列化默认值调整或移除公开旧接口。

根因：两套入口各自处理URL/读取与资源所有权，旧扩展缺取消、泛型响应未释放，已有query/fragment拼接错误。修复层为共享查询构造与平台JSON读取，保留每个入口现有错误策略。

契约：旧公开签名保持；新取消重载传播caller token；泛型入口负责释放response，原始response重载交调用方；旧非JSON/无效JSON/非成功默认值语义保留，LogHttpClient严格异常语义保留。

验收：`dotnet test test/MS.Microservice.Core.Tests/MS.Microservice.Core.Tests.csproj --no-restore -c Release --filter 'FullyQualifiedName~Net.Http|FullyQualifiedName~JsonReuseRegressionTests'`；`./build/validate-api-compatibility.ps1 -Modules @('MS.Microservice.Core') -ReportPath artifacts/validation/api-compatibility-slice07.json`。必要包消费者结果在相关输入不变时复用Slice06。

结果：旧实现新增回归16项中11失败5通过，覆盖query/fragment与空参数、generic各返回路径释放、缺Content-Type；加入取消回归后完整指定HTTP/JSON过滤验收67/67通过，0失败0跳过、退出码0。JsonReuseRegressionTests的同步完成与分配阈值保持通过。

Core及Domain.Primitives/Threading共3包API基线比较全部通过，报告artifacts/validation/api-compatibility-slice07.json。旧公开签名、参数名保留，取消重载为兼容新增。平台HttpContent.ReadFromJsonAsync显式使用原JsonOptions，无新自定义读取wrapper。

复核：两入口共享内部BuildUrl；generic using响应，raw转交所有权；旧默认值与LogHttpClient严格错误策略各自保留。git diff --check通过。Slice06四入口包消费者的源码/依赖输入未因本片改变，复用原8项实际调用证据，不重复运行。

边界：未访问实际HTTP服务；平台JSON读取按HTTP内容编码处理，未改序列化配置或引入重试。
