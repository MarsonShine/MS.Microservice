---
applyTo: "**/*.cs"
---

## General Rules
- 优先复用现有类型与约定；不要重复造轮子
- 保持改动“最小可行”：能局部修改就不全局重构
- 写清楚命名：类名/方法名/参数名表达业务意图
- 不要引入与当前任务无关的模式（例如为简单 CRUD 强行 CQRS/事件总线）
- 对公共 API（public）添加必要的 XML 注释或清晰注释
- 为非平凡逻辑补充单元测试（或至少给出测试用例清单）

## Performance / Safety
- 避免不必要的分配和过深的调用链
- 注意 async/await 正确使用，避免 .Result/.Wait()
- 参数校验清晰：外层（Web）做输入校验，领域层做不变量校验
- 对外暴露的 async 方法，以及所有 I/O / 数据库 / HTTP / 文件 / 消息外部调用，优先显式接收 `CancellationToken ct = default` 并沿调用链传递；除非确实是纯内存计算且无等待点，否则不要省略
- C# / .NET 任务默认按 `csharp-dotnet-development` 的最佳实践处理；把仓库指令当作硬约束，把技能当作 .NET 细节参考，二者要合并后再输出方案
