# 当前状态
更新：2026-11-20
Status: idle
Task: none
Next: 等待支持 EF Core 11 正式版的 PostgreSQL 提供程序发布，再一起评估升级。后续新增或替换的 Azure 部署仍用 Lab 验证。
Baseline: ae2c41c

AspNetCore、Caching 和 Documents 已按模块组织源码和测试，详见[目录整理验收](validation/PROJECT-DIRECTORY-LAYOUT/summary.md)。

.NET 11 官方包已对齐，EF Core 与 CLI 保持兼容的 `10.0.15`，详见[验收记录](validation/NET11-OFFICIAL-PACKAGES/summary.md)。Azure Lab 已验证同一个 `Assistant` 场景可切换两个真实 Chat 部署，见[Azure 验收记录](validation/AZURE-ASSISTANT-LAB/summary.md)。
