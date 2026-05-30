---
applyTo: "{**/*.Core/**/*.cs,**/*.Common/**/*.cs}"
---

## Core / Common Rules
- Core：放跨层契约与通用抽象（接口、Result、错误码、基础类型）
- Common：放通用工具（扩展方法、helper），避免承载业务逻辑
- 避免“上帝模块”：不要把所有东西都塞到 Common/Core
- Core 里的抽象要稳定、面向意图，不要为了当前一次实现临时造接口