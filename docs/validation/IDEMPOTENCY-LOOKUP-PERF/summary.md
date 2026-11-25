# HTTP 幂等重试验收

2026-11-25，以 `ddbe305` 为基线，测量同机 Release 构建中请求指纹的创建，以及未命中、回放、键冲突三种 SQLite 查询。九组场景的逐项耗时与托管分配、输入、采样方法和复现命令见[技术说明与基准表](../../../MS.Microservice.Idempotency/docs/lookup-performance.md)。

| 验收命令（仓库根目录） | 结果 |
| --- | --- |
| `dotnet test MS.Microservice.Idempotency/MS.Microservice.Idempotency.slnx -c Release` | 15 项通过，0 失败，0 跳过 |
| `dotnet test test/MS.Microservice.Reference.Web.Tests -c Release --filter "FullyQualifiedName~Idempotenc\|FullyQualifiedName~Replay\|FullyQualifiedName~KeyedRequest\|FullyQualifiedName~SharedMvcFilter"` | 33 项通过，0 失败，0 跳过 |
| `./build/validate-idempotency-compiled-model.ps1` | SQLite 消费项目成功生成 EF 编译模型并重新编译 |

哈希兼容性测试按原算法计算三列并写入记录，再通过新的公共入口读取。输入包括带零字节或孤立代理字符的身份，以及恰好占 1024 个 UTF-8 字节的身份。查找测试用同一个内存缓存服务承载两个不同模型，并在忽略大小写的 SQLite 列上验证小写指纹仍得到冲突。原有测试继续覆盖空响应、未完成记录、取消、调用方事务回滚、抢占唯一键和过期清理。[模块测试](../../../MS.Microservice.Idempotency/test/MS.Microservice.Idempotency.EFCore.Tests/EfCoreIdempotencyStoreTests.cs)

另以 Npgsql EF 提供程序 10.0.0 生成 SQL，拦截连接打开与执行读取，观察到按作用域与键定位、正文通过指纹 `CASE WHEN` 投影。此检查验证了提供程序翻译；本次未测真实 PostgreSQL 延迟或并发写入吞吐，编译模型检查也未执行 NativeAOT 发布。
