# AI 图片生成 & 编辑流程源代码走读说明

> **适用版本**: `MS.Microservice.AI` 模块  
> **最后更新**: 2026-07-09  
> **目标读者**: 需要理解或修改图片生成/编辑管线的开发者

---

## 目录

1. [架构总览](#1-架构总览)
2. [文生图管线 (Text → Image)](#2-文生图管线-text--image)
3. [参考图编辑管线 (Reference Image Edit)](#3-参考图编辑管线-reference-image-edit)
4. [批量句子生图 (Batch Sentence Generation)](#4-批量句子生图-batch-sentence-generation)
5. [Provider 架构与请求路由](#5-provider-架构与请求路由)
6. [魔法字符串详解](#6-魔法字符串详解)
7. [关键设计决策与原理](#7-关键设计决策与原理)

---

## 1. 架构总览

### 1.1 项目分层

```
MS.Microservice.AI.Abstractions     ← 跨层接口、请求/响应模型、异常类型
MS.Microservice.AI.Core             ← 管线逻辑、Provider 基类、验证器
MS.Microservice.AI.Qwen             ← Qwen/DashScope 具体实现
MS.Microservice.AI.OpenAI           ← OpenAI 具体实现
MS.Microservice.AI.DeepSeek         ← DeepSeek 具体实现
```

### 1.2 图片相关能力

系统支持两种核心图片能力：

| 能力 | 接口 | Provider 方法 | 说明 |
|------|------|---------------|------|
| **文生图** | `IAIImageGenerationClient` | `GenerateAsync` | 从文本描述生成新图片 |
| **图片编辑** | `IAIImageEditClient` | `EditAsync` | 编辑已有图片（支持二进制图片或 URL 参考图） |

### 1.3 图片编辑的两种模式

```
                    ┌─────────────────────────┐
                    │   AIImageEditRequest     │
                    │   ┌───────────────────┐  │
                    │   │ Prompt (required)  │  │
                    │   │ Image (required)   │  │
                    │   │ Mask  (optional)   │  │
                    │   └───────────────────┘  │
                    │                          │
                    │   Binary Image 编辑      │
                    │   (inpainting / bg       │
                    │    removal)              │
                    └─────────────────────────┘

                    ┌──────────────────────────────┐
                    │   ReferenceImageEditRequest   │
                    │   ┌────────────────────────┐  │
                    │   │ Prompt (required)       │  │
                    │   │ ReferenceImageUrl (req) │  │
                    │   │ NegativePrompt (opt)    │  │
                    │   └────────────────────────┘  │
                    │                               │
                    │   参考图 URL 编辑              │
                    │   (通过 IReferenceImageEdit-   │
                    │    Client 独立接口)            │
                    └──────────────────────────────┘
```

- **Binary Image 模式**（OpenAI 兼容）：发送 `multipart/form-data` 到 `/images/edits`，传二进制图片 + Mask + Prompt。`AIImageEditRequest` 保持纯二进制编辑语义，**不含** `ReferenceImageUrl`。
- **Reference Image 模式**（Qwen 多模态）：通过独立的 `IReferenceImageEditClient` 接口 + `ReferenceImageEditRequest`（Core.Images 层），经 `QwenReferenceImageEditAdapter` 适配到 `IQwenImageReferenceEditClient`，发送 JSON 到多模态生成端点。

> **架构要点**：`IReferenceImageEditClient` 和 `IAIImageEditClient` 是**两个独立接口**，分别服务于参考图 URL 编辑和二进制图片编辑。OpenAI-compatible 的 `*ProviderBase` 是 HTTP 复用层，不承载参考图编辑。

---

## 2. 文生图管线 (Text → Image)

### 2.1 总体流程

```
原始文本 (e.g. "Be careful! Don't run in the classroom.")
    │
    ▼
┌──────────────────────────────────────────────┐
│  WordImagePromptPipeline.GeneratePromptsAsync │  ← 入口
└──────────────────────────────────────────────┘
    │
    ├─ ① Parse(rawInput) → WordImageInput
    │     • 分离括号中的 MeaningHint: "apple (fruit)" → TargetText="apple", Hint="fruit"
    │     • 推断卡片类型: Alphabet / Word / Phrase / Sentence / Abstract
    │
    ├─ ② GeneratePlanAsync(input) → WordImagePromptPlan?
    │     • LLM 调用: IPlanGeneratorClient.GenerateVisualPlanAsync()
    │     • 确定性增强: VisualPlanEnricher.Enrich()
    │     • 语义校验: VisualPlanValidator.Validate()
    │     • 自动修复: VisualPlanRepairer.Repair()
    │     • 场景简化: VisualPlanSceneSimplifier.Simplify()
    │     • 合并: MergeVisualPlan() → WordImagePromptPlan
    │
    ├─ ③ EducationalFlashcardPromptBuilder.Build() → RichPrompt (存 DB，不发送)
    │
    └─ ④ QwenSafePromptBuilder.Build() → SafePrompt (发送给生图 API)
```

### 2.2 卡片类型推断 (`InferCardType`)

```csharp
// 规则优先级从高到低：
"a"              → Alphabet   (单个字母)
"apple"          → Word       (纯字母单词)
"keep off grass" → Phrase     (2-3 个单词，无标点)
"Hello, Tom!"    → Sentence   (≥4 个单词 或 含 !?.,/:; 标点)
"123"            → Abstract   (不匹配上述规则)
```

### 2.3 LLM 视觉计划生成 (`GeneratePlanAsync`)

`PlanGeneratorClient` 调用 LLM (`IAIChatClient`)，使用精心设计的 System Prompt 让模型输出结构化的视觉计划 JSON。

**核心概念**：
- LLM 返回 `WordImageVisualPlan`（原始 JSON），包含视觉含义、主体、动作、场景等
- 经过 4 个后处理步骤后合并为 `WordImagePromptPlan`

#### 步骤 1：确定性增强 (`VisualPlanEnricher`)

不依赖 LLM，通过关键词匹配注入规则：

| 触发条件 | 注入内容 |
|----------|----------|
| 含禁止性语言 ("Don't", "No", "Never") | `MustShow`: "the forbidden action itself must be clearly visible" |
| 含 "Be careful" | `SafetyCue`: "near an everyday obstacle, but safe and child-friendly" |
| 含 "run"/"running" | `RequiredAction`: "a child running or about to run" |
| 含 "classroom" | `SceneSetting`: "a bright classroom"; `SettingCues`: "desks" |

#### 步骤 2：语义校验 (`VisualPlanValidator`)

检查 LLM 输出是否语义完整：
- 句子卡片必须有 `RequiredAction`（动作描述）
- 禁止性语句必须有 `ProhibitedAction`（被禁止的动作也要画出）
- 安全提示必须有 `WarningCue` 或 `SafetyCue`
- 如果有跑步相关描述，必须明确展示跑步姿态

#### 步骤 3：自动修复 (`VisualPlanRepairer`)

对校验失败的项自动注入修复值：
- 缺少 `RequiredAction` → 从原始文本提取动作词
- 缺少 `SafetyCue` → 注入通用安全提示

#### 步骤 4：场景简化 (`VisualPlanSceneSimplifier`)

防止 LLM 输出过于花哨：
- `SettingCues` 精简到**最多 1 个**主要环境锚点（如 "slide"、"desk"）
- 移除装饰性杂物关键词（basketball, poster, toy 等）
- 非教室场景自动排除黑板/白板

### 2.4 Prompt 构建器

系统生成**两种 Prompt**，用途不同：

| Prompt 类型 | 构建器 | 特点 | 用途 |
|-------------|--------|------|------|
| **Rich Prompt** | `EducationalFlashcardPromptBuilder` | 包含约束和否定描述 | 存入 DB 做追溯 |
| **Safe Prompt** | `QwenSafePromptBuilder` | **零否定语言、零敏感词** | 发送给 Qwen/DashScope API |

> **为什么需要 Safe Prompt？**  
> Qwen/DashScope 以及任何第三方大模型生成的内容过滤器会扫描 Prompt 文本中的敏感关键词，**即使以否定形式出现也会触发拦截**。例如 "no violence" 会触发 "violence" 过滤。因此 Safe Prompt 必须不包含任何否定词和敏感词。

### 2.5 Hardcoded Negative Elements（硬编码负面元素）

`WordImagePromptPipeline` 中定义了一个 50+ 项的负面元素列表，会在 Merge 时合并到 Plan 中：

```csharp
// 分类：
// 眼睛约束: "dot eyes", "beady eyes", "no iris", "no eye whites"
// 头发与种族: "blonde hair on Chinese children", "red hair on Chinese children"
// 语义失败: "generic standing pose", "unrelated action", "blank studio background"
// 服装: "barefoot", "bare feet", "no shoes", "inappropriate clothing"
// 身体完整性: "cropped head", "cut-off hands", "limbs outside the frame"
// 安全夸张: "injury", "falling", "blood", "crying in pain"
// 敏感内容: "national flags", "weapons", "political symbols"
```

---

## 3. 参考图编辑管线 (Reference Image Edit)

### 3.1 总体流程（结构化 EditDelta 方案）

参考图编辑管线已从旧的"文本解析式 IMAGE EDIT DELTA"迁移到**结构化 `SentenceImageEditDelta` 方案**。核心组件：

```
SceneGroupingAgent.GroupAsync(rows)
    │
    ▼
VisualContextGroup（含 anchor row + members）
    │
    ▼
SentenceEditDeltaAgent.EnrichAsync(group, rows)
    │   LLM 基于 group 上下文 + anchor row 产出 JSON deltas
    │   归一化后写入 member.EditDelta
    │
    ▼
SentenceImageEditPromptBuilder.CanUseReferenceEdit(member.EditDelta)
    │   confidence >= 0.6 && 恰好 1 个 concrete operation
    │
    ├── false → 复用源图 URL（不调用编辑 API）
    │
    └── true
        │
        ▼
SentenceImageEditPromptBuilder.BuildPrompt(delta)
    │   例如: "Only edit: box -> apple."
    │
    ▼
ImageGenerationOrchestrator.GenerateFromReferenceEditDeltaAsync(delta, referenceUrl)
    │
    ├─ ① SentenceImageEditPromptBuilder.CanUseReferenceEdit(delta) → false?
    │     → 返回 ReusedSourceImage=true，复用源图 URL
    │
    ├─ ② IReferenceImageEditClient 未注册？
    │     → throw InvalidOperationException
    │
    ├─ ③ 构建 ReferenceImageEditRequest
    │     • Prompt = SentenceImageEditPromptBuilder.BuildPrompt(delta)
    │     • NegativePrompt = 固定 helper 返回（style transfer, full redraw, ...）
    │
    └─ ④ referenceEditClient.EditReferenceAsync(request)
          → QwenReferenceImageEditAdapter → IQwenImageReferenceEditClient
          → 编辑失败 → 复用源图（不回退独立生图）
```

### 3.2 旧管线 vs 新管线

| 维度 | 旧管线（已移除） | 新管线 |
|---|---|---|
| 入口 | `GenerateFromTextWithReferenceAsync(wordText, url)` | `GenerateFromReferenceEditDeltaAsync(delta, url)` |
| Prompt 来源 | `promptPipeline.GenerateReferenceEditPromptsAsync()` → LLM 文本解析 | `SentenceImageEditPromptBuilder.BuildPrompt(delta)` → 结构化 JSON → 确定性 prompt |
| Negative Prompt | `promptPipeline.GenerateReferenceEditNegativePrompt()` | 固定 helper：`"style transfer, full redraw, new composition, ..."` |
| 上下文注入 | `BuildImageEditContext()` → 括号文本 → `MeaningHint` → LLM 再解析 | `SentenceEditDeltaAgent` → JSON delta → `member.EditDelta` 直接读取 |
| 编辑决策 | `SafePrompt == ""` → 复用源图 | `CanUseReferenceEdit(delta)` → confidence + 单操作检查 |
| 降级策略 | 无明确降级 | 编辑失败 → 复用源图；no-edit delta → 复用源图 |

### 3.3 "空 Delta → 复用源图" 模式

> **核心设计原则**：如果 delta 不可编辑（confidence < 0.6 或非单操作），**绝不**调用图片模型。
> 即使 "keep unchanged" 这类 Prompt 也会导致模型重新编码图片，造成亮度、纹理、对比度漂移。

```csharp
// SentenceImageEditPromptBuilder.CanUseReferenceEdit
public static bool CanUseReferenceEdit(SentenceImageEditDelta? delta)
{
    return delta is { Confidence: >= 0.6 }
        && GetConcreteOperations(delta.Operations).Count == 1;
}
```

不可编辑时，`GenerateFromReferenceEditDeltaAsync` 直接返回 `ReusedSourceImage=true`，`ImageResponse` 中装入源图 URL。

### 3.4 Qwen 多模态 API 实现

#### 请求格式

```json
{
  "model": "qwen-image-edit-max",
  "input": {
    "messages": [{
      "role": "user",
      "content": [
        { "image": "https://cdn.example.com/source.png" },
        { "text": "Use the SOURCE IMAGE as the base canvas. ..." }
      ]
    }]
  },
  "parameters": {
    "n": 1,
    "negative_prompt": "style transfer, full redraw, new composition, changed background, changed character identity, darker image, over-saturated colors, extra objects, unrequested changes",
    "prompt_extend": false,
    "watermark": false,
    "size": "1024*1024"
  }
}
```

#### 端点路由

```
AI:Providers:Qwen:Endpoints:MultimodalGeneration
  = "https://{WorkspaceId}.cn-beijing.maas.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation"
```

- 如果配置了 `Endpoints["MultimodalGeneration"]`，使用绝对 URL
- 否则回退到 `BaseAddress + "api/v1/services/aigc/multimodal-generation/generation"`

#### 响应解析

从 `output.choices[].message.content[].image` 提取 URL：

```json
{
  "output": {
    "choices": [{
      "message": {
        "content": [
          { "image": "https://dashscope.example.com/edited.png" }
        ]
      }
    }]
  }
}
```

#### 关键细节

| 字段 | 处理逻辑 |
|------|----------|
| `size` | `1024x1024` → `1024*1024`（Qwen 要求 `*` 分隔符） |
| `negative_prompt` | 为空时发送**单空格 `" "`**（Qwen API 不接受空字符串） |
| `prompt` | 通过 `WrapEditPromptWithSourceProtection` 包装，**幂等**：若已含前缀则不再重复添加 |
| `prompt_extend` | 固定 `false`（防止模型自行扩展 Prompt 引入意外变化） |
| `watermark` | 固定 `false` |

### 3.5 编辑 Prompt 包装（SOURCE IMAGE Protection）

```csharp
// OpenAICompatibleImageEditProviderBase.WrapEditPromptWithSourceProtection()
// 幂等：如果 prompt 已以 "Use the SOURCE IMAGE as the base canvas." 开头，原样返回
protected static string WrapEditPromptWithSourceProtection(string prompt)
{
    const string prefix = "Use the SOURCE IMAGE as the base canvas.";
    if (prompt.StartsWith(prefix, StringComparison.Ordinal))
        return prompt;

    return prefix + " " +
           "The attached edit instruction is the complete change list. " +
           "All elements outside the named target area remain pixel-faithful to the SOURCE IMAGE. " +
           prompt;
}
```

这些保护词确保 Qwen 编辑模型：
1. 将参考图作为画布基础
2. 只执行 named target area 内的局部编辑
3. 保持其他区域与源图像素级一致

---

## 4. 批量句子生图 (Batch Sentence Generation)

### 4.1 总体流程

```
IReadOnlyList<WordImageRow> (来自 Excel/DB)
    │
    ▼
┌──────────────────────────────────────────┐
│  SentenceImageBatchOrchestrator           │
│  .GenerateBatchAsync()                    │
└──────────────────────────────────────────┘
    │
    ├─ ① ISceneGroupingAgent.GroupAsync(rows)
    │      • Excel 预分组 (SceneGroupId) → 直接使用
    │      • 未分组 → LLM 语义分组
    │
    ├─ ② SentenceEditDeltaAgent.EnrichAsync(group, rows)
    │      • 对每个 grouped rows 调用
    │      • LLM 产出 JSON deltas → 归一化 → member.EditDelta
    │      • 宿主项目应在此步之后保存 group/member/editDelta 到 DB
    │
    ├─ ③ 对每个 Group:
    │      ├─ ShouldUseReferenceEdit == false
    │      │    → 每行独立 GenerateFromTextAsync
    │      │    → Prompt 追加 BuildSceneContext(group, member) 作为括号提示
    │      │
    │      └─ ShouldUseReferenceEdit == true
    │           ├─ 第一行: GenerateFromTextAsync (含场景上下文)
    │           ├─ 保存第一行生成的图片 URL 作为 reference URL
    │           ├─ 第二行起: 读取 member.EditDelta
    │           │   ├─ SentenceImageEditPromptBuilder.CanUseReferenceEdit(editDelta) == false
    │           │   │   → 降级独立生图（不回退到旧 prompt 管线）
    │           │   └─ CanUseReferenceEdit == true
    │           │       ├─ GenerateFromReferenceEditDeltaAsync(delta, referenceUrl)
    │           │       ├─ 成功 → 推进 reference URL
    │           │       └─ 失败 → 复用源图（不推进 reference URL）
    │           │
    │           └─ 返回每行 SentenceImageBatchGenerationResult
    │
    └─ ④ 按原始 OrderIndex 排序返回
```

### 4.2 场景分组 (`SceneGroupingAgent`)

#### 预分组（Excel `SceneGroupId` 列）

Excel 中预先指定的 `SceneGroupId` 直接形成组，**不经过 LLM**。

```csharp
// SplitPreAssigned: 将 rows 分为 preAssigned 和 unassigned
var preGrouped = rows.Where(r => !string.IsNullOrWhiteSpace(r.SceneGroupId))
    .GroupBy(r => r.SceneGroupId!);
// 每个预分组: Confidence=1.0, GroupType="pre_assigned"
```

#### LLM 分组

使用精心设计的 System Prompt 引导 LLM 进行语义分组。

**分组规则（来自 System Prompt）**：

| 规则 | 示例 |
|------|------|
| 相邻问候对 → dialogue 组 | "Hello." / "Hi." |
| 连续自我介绍 → self_introduction 组 | "I am Tom." / "My name is Amy." |
| 连续课堂物品操练 → object_drill 组 | "This is a box." / "This is an apple." |
| 连续地点参观 → location_tour 组 | "It's our classroom." / "It's the library." |
| 同角色跨行出现 → 归入同组 | — |
| 低置信度 → uncertain | Confidence < 0.6 |
| **禁止**将安全规则/运动提示归组 | "Be careful", "Don't skate" 应为 single_sentence |

**内容过滤重试机制**：
- 如果 LLM 调用因 content filter 被拦截，自动用 `PromptSanitizer` 清洗文本后重试
- 清洗策略：去除否定前缀词 + 敏感词列表过滤

### 4.3 参考编辑资格判定 (`SentenceImageReferenceEditPolicy`)

```csharp
public static bool ShouldUseReferenceEdit(VisualContextGroup? group)
{
    if (group == null || group.RowIds.Count <= 1)  return false;    // 单行不需要参考编辑
    if (IneligibleGroupTypes.Contains(groupType))   return false;    // 黑名单
    if (EligibleGroupTypes.Contains(groupType))     return true;     // 白名单
    if (group.Confidence < 0.8)                     return false;    // 低置信度
    return HasStrongContinuityPolicy(...);                           // 连续策略检查
}
```

| 类别 | GroupType 示例 |
|------|----------------|
| ✅ **白名单** | `object_drill`, `dialogue`, `greeting`, `self_introduction`, `location_tour`, `pre_assigned` |
| ❌ **黑名单** | `single_sentence`, `uncertain`, `safety_rules`, `instructional_sequence`, `exercise_sequence`, `sports_safety` |

### 4.4 连续性 Prompt 构建 (`SentenceImageContinuityPromptBuilder`)

#### `BuildSceneContext` — 用于独立生图的场景约束

输出的括号提示被拼接到句末，作为 `MeaningHint` 被 `WordImagePromptPipeline.Parse` 解析：

```
"This is a box. (Current sentence image only. Shared context type: object drill.
 Stable scene: classroom table. Current sentence visual focus: a box.)"
```

关键控制子句：
- `"Current sentence image only; do not combine objects or actions from other sentences"`
- `"Shared context type: object drill"`
- `"Stable scene: classroom table"`
- `"Current sentence visual focus: a box"`
- `"Keep the stable scene and characters consistent"`

#### 结构化 EditDelta — 用于参考编辑（替代旧 `BuildImageEditContext`）

**旧方案**（已移除）：`BuildImageEditContext` 生成文本式的 `IMAGE EDIT DELTA` 括号提示，依赖 LLM 文本解析。

**新方案**：使用结构化 `SentenceImageEditDelta` JSON：
```json
{
  "rowId": 2,
  "referenceRowId": 1,
  "confidence": 0.95,
  "operations": [
    { "operation": "replace", "from": "box", "to": "apple" }
  ]
}
```

`SentenceImageEditPromptBuilder.BuildPrompt(delta)` 将其转换为最小编辑 prompt：
```
"Only edit: box -> apple."
```

这比"保持场景不变，只改当前目标"更精确、更可审计。

### 4.5 参考 URL 推进规则

> **关键规则**：如果某行的编辑操作结果是 **ReusedSourceImage=true**（没有实际视觉变化），参考 URL **不更新**。下一行仍然以最后一张实际发生过视觉变化的图片作为 reference。

```csharp
// SentenceImageBatchOrchestrator 核心逻辑：
if (!editResult.ReusedSourceImage)
{
    var newImageUrl = GetImageUrl(editResult.ImageResponse);
    if (!string.IsNullOrWhiteSpace(newImageUrl))
    {
        lastReferenceUrl = newImageUrl;  // 更新 reference URL
    }
}
```

这样避免了因为某行没有实际编辑而导致后续行的参考图与实际画面错位。

> **注意**：只使用 `AIImageResponse.Images[].Url` 作为 reference URL，不使用 `Content.FileName`。

---

## 5. Provider 架构与请求路由

### 5.1 类继承层次

```
OpenAICompatibleMediaProviderBase          ← 基类：HTTP 请求、重试、错误处理、并发门控
    │
    └── OpenAICompatibleImageGenerationProviderBase   ← 图片生成基类
            │
            └── OpenAICompatibleImageEditProviderBase ← 图片编辑基类
                    │
                    ├── QwenImageEditProvider          ← Qwen 实现
                    └── OpenAIImageEditProvider         ← OpenAI 实现
```

### 5.2 请求路由

```
IAIImageEditClient.EditAsync(request)
    │
    ▼
RoutingAIImageEditClient.EditAsync()
    ├─ AIRequestValidator.ValidateImageEditRequest()   ← 校验
    ├─ IAIModelResolver.ResolveImageEditModel()        ← 解析 Provider/Model
    ├─ IAIProviderFactory.GetRequiredImageEditProvider()← 获取 Provider 实例
    └─ provider.EditAsync(model, request)              ← 委托执行
```

### 5.3 错误处理

`OpenAICompatibleMediaProviderBase` 提供了两层错误解析：

```csharp
// 第一层：OpenAI 兼容格式
{ "error": { "message": "...", "type": "...", "code": "..." } }

// 第二层（回退）：Qwen 多模态格式
{ "code": "InvalidParameter", "message": "...", "request_id": "req-abc-123" }
```

HTTP 状态码映射：

| 状态码 | 抛出异常 |
|--------|----------|
| 400 | `AIProviderException` + `AIErrorCodes.InvalidRequest` |
| 401/403 | `AIProviderException` + `AIErrorCodes.ProviderAuthenticationFailed` |
| 429 | `AIRateLimitException` |
| 408/504 | `AIProviderException` + `AIErrorCodes.ProviderTimeout` |
| 5xx | `AIProviderException` + `AIErrorCodes.ProviderUnavailable` (可重试) |
| content_filter | `AIContentSafetyException` |

### 5.4 重试机制

- 自动重试可瞬态错误（429 / 5xx / 超时）
- 重试次数由 `AIResolvedModel.MaxRetryAttempts` 控制
- 指数退避：`min(500 * 2^(attempt-1), 2000)` ms

---

## 6. 魔法字符串详解

### 6.1 PromptSanitizer 敏感词列表

`PromptSanitizer.Clean()` 用于清洗送往 Qwen 的 Prompt，**去掉所有可能触发内容过滤的词汇**（即使以否定形式出现）。

```csharp
// 否定短语清洗 — 通过 Regex 移除
"no violence"           → ""         // NegationRegex: \bno\s+\w+...
"never run"             → ""         // NeverRegex
"without shoes"         → ""         // WithoutRegex
"don't touch"           → ""         // DontRegex
"do not enter"          → ""         // DoNotRegex

// 敏感词清洗 — 逐词移除
"violence" → "", "blood" → "", "injury" → "", "weapon" → ""
"barefoot" → "", "cropped" → "", "decapitated" → ""
"flag" → "", "political" → "", "religious symbol" → ""
```

**设计原理**：Qwen/DashScope 的内容过滤器是**关键词匹配**而非语义理解。在 Prompt 中出现 "no violence" 时，过滤器只看到 "violence" 就拦截，不理解否定语义。因此必须完全移除这些词。

### 6.2 QwenSafePromptBuilder 固定样式头

每个 Safe Prompt 都以三个固定语句开头：

```
1. "A simple 4:3 horizontal illustration in bright cheerful children's storybook style
    with clean smooth lines and flat soft colors."
   → 定义画风：4:3 横版、明亮童书风格、干净线条、平面柔和色彩

2. "Completely text-free image with no Chinese characters, English letters, numbers,
    punctuation, or readable markings anywhere."
   → 强制无文字（教育闪卡场景的核心要求）

3. "Characters have natural expressive eyes with clear irises, visible eye whites,
    and small highlights instead of tiny dot eyes or bean-like eyes."
   → 约束眼睛画法（AI 模型倾向于画豆豆眼，不符合教材标准）
```

### 6.3 固定收尾语句

```
"Medium-wide balanced composition with all characters fully visible from head to toe."
"Characters wear everyday clothing and appropriate shoes suitable for the scene."
"Comfortable margins around all subjects, nothing touching the frame edges."
"Cheerful warm atmosphere with gentle daylight and soft fresh colors."
```

### 6.4 参考编辑 Negative Prompt 固定项

`BuildReferenceEditNegativePrompt` 生成的标准 negative prompt：

```
style transfer, full redraw, new composition, different camera angle,
changed background, changed character identity, darker image,
over-saturated colors, higher contrast, dramatic shadows, color grading,
vignette, glow, haze, extra objects, unrequested changes
```

**条件追加项**：
- 如果编辑请求不包含 "arrow" → 追加 `"arrows, pointer marks"`
- 如果编辑请求不包含 "text"/"label" → 追加 `"new text, captions, labels"`

### 6.5 控制子句标记 (Control Clause Markers)

`SentenceImageContinuityPromptBuilder` 生成的括号提示中包含结构化的控制子句，`QwenSafePromptBuilder` 通过识别这些标记来解析编辑意图：

```
触发识别: "IMAGE EDIT DELTA" | "Current sentence image only" | "Prompt branch" | "Shared context type"

分类解析:
├── 编辑指令 (IsExplicitEditInstructionClause):
│     "Replace or revise only", "Only add or update", "Only adjust the visible state",
│     "Change only", "Replace ", "Revise ", "Update ", "Add ", "Remove "
│
├── 源图描述 (IsSourceLocatorClause):
│     "Reference image currently illustrates", "Reference row visual focus",
│     "Reference row visual action", "Reference row variable elements"
│
├── 目标描述 (IsTargetStateClause):
│     "Current target visual focus", "Current target visual action",
│     "Current row variable elements", "Current sentence visual focus",
│     "Current sentence visual action", "Sentence-specific variable elements"
│
└── 不安全编辑子句 (IsUnsafeBroadEditClause) — 被过滤掉:
      "Remove or soften reference-row details", "Prompt branch", "Depict ",
      "Current target sentence", "Current sentence image only", "Shared context type"
```

### 6.6 Setting Cues 限制

```csharp
// QwenSafePromptBuilder (Safe Prompt): 最多 2 个
plan.SettingCues.Select(Clean).Where(non-null).Take(2)

// VisualPlanEnricher (增强): 最多 1 个
PromptNormalizer.NormalizeList(plan.SettingCues, 1)

// VisualPlanSceneSimplifier (简化): 精简到最多 1 个主要锚点
var primaryCue = cues.FirstOrDefault(ContainsAny, PrimaryEnvironmentAnchors) ?? cues.FirstOrDefault();

// EducationalFlashcardPromptBuilder (Rich Prompt): 最多 1 个
plan.SettingCues.Take(1)
```

**原理**：Setting Cues 太多会导致 AI 模型画出一堆不必要的环境细节，分散注意力。

---

## 7. 关键设计决策与原理

### 7.1 为什么有 Rich Prompt 和 Safe Prompt 两套？

| | Rich Prompt | Safe Prompt |
|---|---|---|
| **内容** | 含约束、否定、禁止项 | 纯正面描述 |
| **发送给 API** | ❌ 不发送 | ✅ 发送 |
| **存入 DB** | ✅ 存入 | 可选 |
| **目的** | 开发者追溯/调试 | 通过内容安全过滤 |

**核心原因**：Qwen/DashScope 的内容过滤器是关键词级别的，不理解语义否定。Rich Prompt 中大量的 "no", "don't", "avoid", "barefoot" 等词会直接触发拦截。

### 7.2 为什么空 Delta 不调用编辑 API？

AI 图片编辑模型的 "identity edit"（无修改编辑）不是真正的无操作——**模型会对整张图重新编码**，导致：
- 亮度漂移（通常变暗）
- 纹理细节损失（水彩/故事书风格退化）
- 对比度和色温偏移
- 不必要的 API 调用费用

因此 `QwenSafePromptBuilder.BuildReferenceEditPrompt()` 在没有具体编辑指令时返回 `string.Empty`，上游检测到此值后直接复用源图 URL。

### 7.3 为什么 Negative Prompt 空时发送空格而不是空字符串？

Qwen 多模态 API 的 `negative_prompt` 参数不接受空字符串 `""`。发送单空格 `" "` 是经测试验证的兼容方案。

### 7.4 为什么 Setting Cues 要严格限制数量？

AI 图片模型倾向于把 Prompt 中的每个名词都画出来。Setting Cues 超过 2 个时，模型容易：
- 画出与被禁内容无关的额外设施
- 场景变得拥挤混乱
- 失去教学插图应有的简洁焦点

因此管线在多个阶段强制执行 `.Take(1)` 或 `.Take(2)` 限制。

### 7.5 为什么批量生图不并发处理同组？

同一 `VisualContextGroup` 内的行共享角色、场景和视觉风格。并发生图时：
- 无法保证后续行能引用到前一行生成的图片 URL
- 参考编辑的连续性语义依赖于前一行已生成的实际图片

因此 `SentenceImageBatchOrchestrator` 在同组内**严格按 OrderIndex 顺序串行处理**。

---

## 附录：文件索引

| 文件 | 职责 |
|------|------|
| `AIImageEditRequest.cs` | 图片编辑请求模型（支持 binary / URL 两种模式） |
| `AIRequestValidator.cs` | 请求校验（互斥校验、URL scheme 校验） |
| `AIOptions.cs` | 配置模型（含 `Endpoints` 字典） |
| `OpenAICompatibleMediaProviderBase.cs` | Provider 基类（HTTP、重试、错误解析） |
| `OpenAICompatibleImageEditProviderBase.cs` | 编辑 Provider 基类（multipart 请求、SOURCE IMAGE 保护） |
| `QwenImageEditProvider.cs` | Qwen 编辑实现（多模态 JSON 路径 + multipart 回退） |
| `WordImagePromptPipeline.cs` | 文生图 Prompt 管线入口 |
| `WordImageInput.cs` | 解析后的输入模型（TargetText + MeaningHint + ContentType） |
| `WordImageVisualPlan.cs` | LLM 原始输出模型 |
| `WordImagePromptPlan.cs` | 合并后的计划模型（LLM + 增强 + 校验修复 + 简化） |
| `VisualPlanEnricher.cs` | 确定性规则增强 |
| `VisualPlanValidator.cs` | 语义校验 |
| `VisualPlanRepairer.cs` | 自动修复 |
| `VisualPlanSceneSimplifier.cs` | 场景简化 |
| `EducationalFlashcardPromptBuilder.cs` | Rich Prompt 构建（存 DB） |
| `QwenSafePromptBuilder.cs` | Safe Prompt 构建（发 API）+ Reference Edit Prompt + Negative Prompt |
| `PromptSanitizer.cs` | 敏感词/否定词清洗 |
| `PromptNormalizer.cs` | Prompt 值规范化工具 |
| `ImageGenerationOrchestrator.cs` | 单条文生图 + 参考编辑入口 |
| `SentenceImageBatchOrchestrator.cs` | 批量句子生图编排 |
| `SceneGroupingAgent.cs` | LLM 场景分组 |
| `SentenceImageContinuityPromptBuilder.cs` | 连续性场景上下文 / 编辑上下文构建 |
| `SentenceImageReferenceEditPolicy.cs` | 参考编辑资格判定 |
| `SentenceImageReferenceContext.cs` | 参考图上下文模型 |
| `ImageGenerationResultTypes.cs` | 结果类型（ReferenceImageEditResult 等） |
