# MS.Microservice.AI — Image Generation Pipeline

英语教学卡片图片 Prompt 生成类库。将原始文本输入（单词、短语、句子）转换为高质量的生图 prompt，支持两路输出：

- **Rich prompt** — 含完整约束、否定词、语义分析，存入数据库用于追溯
- **Safe prompt** — 纯正向、零敏感词，直接发送给文生图 API

该模块已集成到 `MS.Microservice.AI.Core` 框架中，与现有的 `IAIImageGenerationClient` / `IAIImageGenerationProvider` 体系无缝协作。

---

## 与框架的集成架构

```
                             ┌──────────────────────────────┐
                             │        appsettings.json      │
                             │  AI:Models:Chat:             │
                             │    ImagePromptPlanning       │
                             │  AI:Models:ImageGeneration:  │
                             │    Default                   │
                             └──────────────┬───────────────┘
                                            │
              ┌─────────────────────────────┼─────────────────────────┐
              │                             ▼                         │
              │  ┌──────────────────────────────────────────────┐    │
              │  │         ImageGenerationOrchestrator           │    │
              │  │  GenerateFromTextAsync(wordText) → result     │    │
              │  └────────┬──────────────────┬──────────────────┘    │
              │           │                  │                       │
              │           ▼                  ▼                       │
              │  ┌────────────────┐  ┌──────────────────────┐       │
              │  │ WordImagePrompt │  │ IAIImageGeneration   │       │
              │  │    Pipeline     │  │      Client           │       │
              │  │  (prompt 生成)  │  │  (Routing Client)     │       │
              │  └───────┬────────┘  └──────────┬───────────┘       │
              │          │                       │                   │
              │          ▼                       ▼                   │
              │  ┌────────────────┐  ┌──────────────────────┐       │
              │  │ PlanGenerator   │  │ IAIModelResolver     │       │
              │  │    Client       │  │ (scenario → model)   │       │
              │  │ (IAIChatClient) │  └──────────┬───────────┘       │
              │  └───────┬────────┘             │                   │
              │          │                      ▼                   │
              │          │         ┌──────────────────────┐         │
              │          │         │ IAIProviderFactory    │         │
              │          │         │ .GetRequiredImage-    │         │
              │          │         │  GenerationProvider() │         │
              │          │         └──────────┬───────────┘         │
              │          │                    │                     │
              │          ▼                    ▼                     │
              │  ┌────────────────┐  ┌──────────────────────┐       │
              │  │ RoutingAIChat  │  │ OpenAICompatible-     │       │
              │  │    Client      │  │ ImageGeneration-      │       │
              │  │ (scenario:     │  │ ProviderBase          │       │
              │  │  ImagePrompt   │  │ (images/generations)  │       │
              │  │  Planning)     │  └──────────────────────┘       │
              │  └────────────────┘                                 │
              │                                                     │
              │  MS.Microservice.AI.Core                            │
              └─────────────────────────────────────────────────────┘
```

**关键集成点：**

| 层级 | 组件 | 作用 |
|---|---|---|
| **编排层** | `ImageGenerationOrchestrator` | 一站式：文本 → prompt → 图片（教育图片应用层） |
| **Prompt 层** | `WordImagePromptPipeline` | LLM 视觉规划 + prompt 构建 |
| **Chat 路由** | `IAIChatClient` → `RoutingAIChatClient` | 通过 scenario `ImagePromptPlanning` 解析 LLM 模型 |
| **Image 路由** | `IAIImageGenerationClient` → `RoutingAIImageGenerationClient` | 通过 scenario `Default` 解析生图模型 |
| **参考图编辑** | `IReferenceImageEditClient` → provider adapter | 参考图编辑需 provider adapter（当前由 Qwen 提供） |
| **Provider 层** | `OpenAICompatibleImageGenerationProviderBase` | 调用 `/v1/images/generations` API |

---

## 配置

在 `appsettings.json` 中配置 prompt 规划模型（Chat capability）和图片生成模型（ImageGeneration capability）：

```json
{
  "AI": {
    "DefaultProvider": "OpenAI",
    "Providers": {
      "OpenAI": {
        "ApiKeySecretName": "AI_OPENAI_API_KEY",
        "BaseAddress": "https://api.openai.com/v1/",
        "TimeoutSeconds": 120
      },
      "Qwen": {
        "ApiKeySecretName": "AI_QWEN_API_KEY",
        "BaseAddress": "https://dashscope.aliyuncs.com/compatible-mode/v1/",
        "TimeoutSeconds": 120
      }
    },
    "Models": {
      "Chat": {
        "ImagePromptPlanning": {
          "Provider": "OpenAI",
          "Model": "gpt-4.1-mini",
          "TimeoutSeconds": 60
        }
      },
      "ImageGeneration": {
        "Default": {
          "Provider": "OpenAI",
          "Model": "gpt-image-1",
          "Size": "1024x1024",
          "Quality": "standard"
        }
      }
    }
  }
}
```

### Qwen 参考图编辑配置

参考图编辑是 Qwen-specific 能力，通过 `IReferenceImageEditClient` adapter 提供（需 `AddQwen()`）：

```json
{
  "AI": {
    "Providers": {
      "Qwen": {
        "ApiKeySecretName": "AI_QWEN_API_KEY",
        "BaseAddress": "https://dashscope.aliyuncs.com/compatible-mode/v1/",
        "Endpoints": {
          "MultimodalGeneration": "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation"
        }
      }
    },
    "Models": {
      "ImageEdit": {
        "QwenReferenceEdit": {
          "Provider": "Qwen",
          "Model": "qwen-image-edit-plus",
          "Size": "1024*1024",
          "Count": 1
        }
      }
    }
  }
}
```

> **注意**：`AIImageEditRequest` 是通用二进制图片编辑（inpainting / background removal），不承载参考图 URL。
> Qwen 参考图编辑使用独立的 `IQwenImageReferenceEditClient` / `IReferenceImageEditClient`。
> OpenAI-compatible 层是 provider HTTP 复用层（`OpenAICompatibleImageGenerationProviderBase`），不是参考图编辑通道。

### Scenario 说明

| Scenario | Capability | 用途 |
|---|---|---|
| `ImagePromptPlanning` | Chat | LLM 视觉规划（可自定义，传入 `AddImagePromptPipeline("MyScenario")`) |
| `Default` | ImageGeneration | 实际图片生成（可通过 `AIImageGenerationRequest.Scenario` 覆盖） |
| `QwenReferenceEdit` | ImageEdit | Qwen 参考图编辑（需 `AddQwen()` adapter） |

---

## 依赖说明

| 包 | 用途 |
|---|---|
| `Microsoft.Extensions.Logging.Abstractions` | 日志 |
| `MS.Microservice.AI.Abstractions` | AI 能力抽象 |
| `MS.Microservice.AI.Core` | 路由、模型解析 |
| `MS.Microservice.Core` | 核心工具（`DefaultSerializeSetting` 等），**允许依赖** |

```
Images/
├── Models/                              # 纯数据模型
│   ├── WordImageCardType.cs             # 卡片类型常量
│   ├── WordImageInput.cs                # 解析后的输入
│   ├── WordImageRow.cs                  # 批量输入行（Excel/DB）
│   ├── WordImagePromptPlan.cs           # 最终合并计划
│   ├── WordImageVisualPlan.cs           # LLM 原始输出的结构化视觉计划
│   ├── VisualContextGroup.cs            # 视觉上下文组
│   ├── VisualContextMember.cs           # 组成员（行级视觉指引，含 EditDelta）
│   ├── CharacterProfile.cs              # 角色外观画像
│   ├── SentenceImageEditDelta.cs        # 结构化编辑 Delta
│   ├── SentenceImageEditOperation.cs    # 单个编辑操作
│   ├── ReferenceImageEditRequest.cs     # 参考图编辑请求 DTO
│   └── ImageGenerationResultTypes.cs    # 生成结果类型
│
├── IPlanGeneratorClient.cs
├── ISceneGroupingAgent.cs
├── IReferenceImageEditClient.cs         # 参考图编辑客户端接口（Core.Images 层）
├── PlanGeneratorClient.cs
├── SceneGroupingAgent.cs
├── WordImagePromptPipeline.cs           # 主编排器：Parse → Plan → Enrich → Build
├── ImageGenerationOrchestrator.cs       # 一站式编排器：文本 → prompt → 图片 / delta → 参考图编辑
├── SentenceEditDeltaAgent.cs            # 结构化编辑 Delta Agent
├── SentenceImageBatchOrchestrator.cs    # 批量句子生图编排器
│
├── Pipeline/
│   ├── VisualPlanEnricher.cs
│   ├── VisualPlanValidator.cs
│   ├── VisualPlanRepairer.cs
│   └── VisualPlanSceneSimplifier.cs
│
├── Building/
│   ├── EducationalFlashcardPromptBuilder.cs
│   ├── QwenSafePromptBuilder.cs
│   ├── SentenceImageContinuityPromptBuilder.cs  # 共享场景上下文构建
│   ├── SentenceImageEditPromptBuilder.cs       # 结构化 Delta → 编辑 Prompt
│   ├── SentenceImagePromptBranchComposer.cs
│   └── SentenceImageReferenceEditPolicy.cs
│
├── Analysis/
│   └── SentenceSemanticAnalyzer.cs
│
└── Helpers/
    ├── PromptSanitizer.cs
    └── PromptNormalizer.cs
```

## 数据流

```
输入: "Be careful! Don't run in the classroom."
  │
  ▼
Parse() → WordImageInput {
    RawInput: "Be careful! Don't run in the classroom."
    TargetText: "Be careful! Don't run in the classroom."
    MeaningHint: null
    ContentType: "sentence"
}
  │
  ▼
IPlanGeneratorClient.GenerateVisualPlanAsync() → WordImageVisualPlan {
    visualMeaning: "A child running between desks while a teacher gestures to stop..."
    sentenceIntent: "prohibition"
    primaryActor: "child"
    secondaryActor: "teacher"
    requiredAction: "..."
    prohibitedAction: "running"
    warningCue: "teacher raises palm in stopping gesture"
    safetyCue: "child near desks showing why running is unsafe"
    sceneSetting: "a bright classroom"
    settingCues: ["desks", "chairs", "windows", "blank board"]
    ...
}
  │
  ▼
VisualPlanEnricher.Enrich()          ← 确定性规则注入 mustShow/mustNotShow
  │
  ▼
VisualPlanValidator.Validate()       ← 检查禁止句/安全句/教室/跑步是否完备
  │ (如有缺失)
  ▼
VisualPlanRepairer.Repair()          ← 自动修复缺失项
  │
  ▼
VisualPlanSceneSimplifier.Simplify() ← 场景去冗余（剔除教室/杂乱关键词，仅保留一个核心环境锚点）
  │
  ▼
MergeVisualPlan() → WordImagePromptPlan  ← 合并 LLM 输出 + 规则补强 + 修复 + 去冗余结果
  │
  ├──▶ EducationalFlashcardPromptBuilder.Build()  → richPrompt (存 DB)
  │
  └──▶ QwenSafePromptBuilder.Build()              → safePrompt (发 Qwen)
```

## 双 Prompt 对比

以 `Keep off the grass.` 为例：

| 维度 | Rich Prompt (存 DB) | Safe Prompt (发 Qwen) |
|---|---|---|
| 长度 | ~500 words | ~80 words |
| 风格 | 约束式 ("No...", "Avoid...", "Strictly...") | 纯正向描述 |
| 敏感词 | 包含 (barefoot, injury, violent, sexual...) | 零 |
| 用途 | 调试追溯、审计 | 实际生图 |

### Rich Prompt 片段
> ...No flags, flag symbols, national emblems, political symbols, military elements, maps with borders, violent, sexual, hateful, disturbing, or adult content. All people must wear appropriate footwear — strictly no bare feet...

### Safe Prompt 片段
> A simple 4:3 horizontal illustration in bright cheerful children's storybook style... A child walks beside a grassy lawn while a nearby adult gently signals to stay on the path... The scene takes place in a park path next to a lawn. Recognizable details include paved walkway, green grass area...

---

## 宿主项目集成

### 方式一：一站式编排器（推荐） — 普通生图

使用 `ImageGenerationOrchestrator` 一步完成"文本 → prompt → 图片"的完整流程：

```csharp
// Program.cs — DI 注册
builder.Services.AddMicroserviceAI(builder.Configuration)
    .AddOpenAI()        // 或其他 Provider
    .AddQwen()          // 如需 Qwen 参考图编辑
    .Services
    .AddImagePromptPipeline();

// YourService.cs — 使用
public class FlashcardService
{
    private readonly ImageGenerationOrchestrator orchestrator;

    public FlashcardService(ImageGenerationOrchestrator orchestrator)
    {
        this.orchestrator = orchestrator;
    }

    public async Task<ImageGenerationResult> GenerateCardImageAsync(string wordText)
    {
        return await orchestrator.GenerateFromTextAsync(wordText);
    }
}
```

### 方式二：参考图编辑（基于结构化 EditDelta）

```csharp
// SentenceEditDeltaAgent 写入 member.EditDelta
var agent = provider.GetRequiredService<SentenceEditDeltaAgent>();
await agent.EnrichAsync(group, rows);

// ImageGenerationOrchestrator 执行参考图编辑
var editResult = await orchestrator.GenerateFromReferenceEditDeltaAsync(
    member.EditDelta!, referenceImageUrl);

// editResult.ReusedSourceImage → true 表示复用源图
// editResult.ImageResponse.Images[0].Url → 编辑后的图片 URL
```

### 方式三：批量句子生图（完整流程）

```csharp
// Step 1: 场景分组
var groupingAgent = provider.GetRequiredService<ISceneGroupingAgent>();
var groupingResult = await groupingAgent.GroupAsync(rows);

// Step 2: 结构化 EditDelta 注入
var deltaAgent = provider.GetRequiredService<SentenceEditDeltaAgent>();
foreach (var group in groupingResult.Groups)
{
    await deltaAgent.EnrichAsync(group, rows);
}

// Step 3: 批量生图（内部自动处理 reference edit 逻辑）
var batchOrchestrator = provider.GetRequiredService<SentenceImageBatchOrchestrator>();
var results = await batchOrchestrator.GenerateBatchAsync(rows);
```

> **注意**：不要在宿主项目中直接注入 `DashScopeService` 做图生图。以下职责留在宿主项目：
> - DB 查询、已有图片 URL 判断
> - OSS/CDN 上传、resize
> - 状态码更新
> 
> AI 模块只返回 `AIImageResponse`、prompt、是否复用源图、是否使用 reference edit。

## 关键设计决策

### 1. 为什么使用 Scenario 而非硬编码 Model

`PlanGeneratorClient` 通过 `AIChatRequest.Scenario` 触发框架的 `IAIModelResolver` 模型解析，而非硬编码模型名。这意味着：

- 与框架其他 capability（Chat, TTS, ImageGeneration）使用**同一配置模式**
- 模型切换只需修改 `appsettings.json`，无需重新编译
- 支持 per-environment 配置（开发/测试用 cheap model，生产用 full model）

```csharp
// 默认 scenario = "ImagePromptPlanning"
services.AddImagePromptPipeline();

// 或使用自定义 scenario
services.AddImagePromptPipeline("MyCustomImagePlanner");
```

### 2. 为什么 Safe Prompt 不能简单去掉否定词

Qwen/DashScope 的内容审核是基于 prompt **文本本身**做关键词扫描的，不看语义意图。以下 prompt 仍然会被拦截：

```
Do not show any violence, blood, or weapons.
```

因为 `violence`, `blood`, `weapons` 三个词出现在了文本中，即使前面有 "Do not show"。

`QwenSafePromptBuilder` 使用 `PromptSanitizer` 完全剔除所有敏感词，并用 `CleanNegativeLanguage` 将否定约束转换为正向描述。

### 3. 为什么 LLM Planner + 确定性规则

LLM 规划的视觉计划可能遗漏关键语义元素（例如忘记画禁止动作、忘记画制止线索）。`VisualPlanEnricher` 用正则表达式检测句子类型，强制注入缺失的 `mustShow`/`mustNotShow` 约束。`VisualPlanValidator` 在 prompt 组装前做最终检查，`VisualPlanRepairer` 自动修复问题。

### 4. 为什么需要 IPlanGeneratorClient 抽象

将 LLM 调用抽象为接口的好处：
- 类库不依赖 `Azure.AI.OpenAI` / `OpenAI.Chat` 等重型包
- 可替换 LLM 提供商（Azure OpenAI → 其他）
- 单元测试可用 mock 实现

### 5. 为什么引入 ImageGenerationOrchestrator

`ImageGenerationOrchestrator` 将"prompt 规划"和"图片生成"两个步骤编排为一个原子操作：

| 无编排器 | 有编排器 |
|---|---|
| 手动调用 `WordImagePromptPipeline` → 得到 prompt | `orchestrator.GenerateFromTextAsync(text)` → 得到图片 |
| 手动构建 `AIImageGenerationRequest` → 调用 `IAIImageGenerationClient` | 一站式调用，统一的错误处理和 fallback |
| 需自行管理 rich prompt 存储 | `ImageGenerationResult` 同时返回 rich prompt + 图片 |

### 6. 反冗余设计（Anti-Clutter Design）

为避免生成图片中出现过多无关元素（如多个娱乐设施、不应出现的黑板等），系统引入了多层防护：

| 层级 | 机制 | 说明 |
|---|---|---|
| **LLM Prompt** | Rule 9, 12, 13, 16 | 黑板仅限教室场景、安全句仅选一个焦点、settingCues 1-3 个、cue 是环境描述不是主体 |
| **LLM Prompt** | Example 2 | 提供"Be careful! Play safely."正确示例：仅滑梯一个游乐设施，无黑板 |
| **VisualPlanEnricher** | 归一化 | `NormalizeList(plan.SettingCues, 1)` — 无论 LLM 返回几个 cue，最多保留 1 个 |
| **VisualPlanSceneSimplifier** | 关键词剔除 | `SimplifySettingCues()` 剔除 ClassroomCueKeywords（board/blackboard/desk…）和 ClutterCueKeywords（basketball/swing/poster…），仅保留 1 个 `PrimaryEnvironmentAnchor`（slide/road/bench…） |
| **VisualPlanSceneSimplifier** | MustShow 精简 | 剔除 clutter 关键词；安全句额外过滤 "multiple equipment" / "several" / "various" |
| **VisualPlanSceneSimplifier** | SupportingVisual 精简 | 非教室句剔除教室关键词；无关 clutter 剔除 |
| **VisualPlanSceneSimplifier** | MustNotShow 注入 | 非教室句注入 blackboard/whiteboard/classroom board；所有句注入 extra playground equipment / unrelated sports balls / decorative props |
| **QwenSafePromptBuilder** | `.Take(2)` | Safe prompt 中 settingCues 最多保留 2 个 |
| **EducationalFlashcardPromptBuilder** | minimal props | "every object must contribute to understanding" 约束 |

`VisualPlanSceneSimplifier` 是管线 Step 3（紧随 Enrich → Validate → Repair 之后），作为最后一道确定性防线，在 prompt 组装前确保场景简洁、聚焦、无多余元素。

### 7. 场景分组与批量生成（Scene Grouping & Batch Generation）

`SceneGroupingAgent` 提供 LLM 驱动的句子分组能力，用于批量图片生成时保持组内视觉一致性：

```
Excel/DB 行数据 → SceneGroupingAgent.GroupAsync()
    ├── 预分配组 (SceneGroupId 列) → 直接分组，confidence=1.0
    └── 未分配行 → LLM 语义分组
        ├── dialogue / greeting / object_drill → 共享角色+场景
        ├── instructional_sequence / safety_rules → 独立单句
        └── 低置信度行 → 标记为 uncertainRowIds

组内图片生成策略（SentenceImageReferenceEditPolicy）：
    ├── object_drill / dialogue / greeting → 结构化 EditDelta + 参考图编辑
    ├── safety_rules / instructional_sequence → 各自独立生成
    └── 组内句子 → SentenceImageContinuityPromptBuilder 注入共享上下文

完整批量流程：
    ├── SceneGroupingAgent.GroupAsync(rows)
    ├── SentenceEditDeltaAgent.EnrichAsync(group, rows)
    ├── 保存 group/member/editDelta 到 DB
    └── SentenceImageBatchOrchestrator.GenerateBatchAsync(rows, ...)
```

**配置方式**：`AddImagePromptPipeline()` 自动注册 `SceneGroupingAgent` 为 Singleton。`SentenceEditDeltaAgent` 和 `SentenceImageBatchOrchestrator` 均为 Transient。

### 8. 组件接口概览

| 类型 | 生命周期 | 职责 |
|---|---|---|
| `IPlanGeneratorClient` → `PlanGeneratorClient` | Singleton | LLM 视觉规划（alphabet / visual plan） |
| `ISceneGroupingAgent` → `SceneGroupingAgent` | Singleton | LLM 句子场景分组 |
| `WordImagePromptPipeline` | Transient | 文本解析 → 视觉规划 → prompt 构建 |
| `SentenceEditDeltaAgent` | Transient | 结构化编辑 Delta 注入（LLM → EditDelta） |
| `ImageGenerationOrchestrator` | Transient | 一站式：文本 → prompt → 图片 / delta → 参考图编辑 |
| `SentenceImageBatchOrchestrator` | Transient | 批量句子生图编排（场景分组 + 参考图编辑） |

### 9. 结构化参考图编辑数据流（SentenceEditDeltaAgent）

新的结构化方案替代了旧的 `IMAGE EDIT DELTA` 文本注入。数据流如下：

```
SceneGroupingAgent.GroupAsync(rows)
    │
    ▼
VisualContextGroup (含 anchor row + members)
    │
    ▼
SentenceEditDeltaAgent.EnrichAsync(group, rows)
    │   LLM 基于 group 上下文 + anchor row 产出 JSON deltas
    │   归一化后写入 member.EditDelta
    │
    ▼
SentenceImageEditPromptBuilder.CanUseReferenceEdit(member.EditDelta)
    │   confidence >= 0.6 && 恰好 1 个 concrete operation → 可用
    │
    ├── false → 复用源图 URL 或降级独立生图
    │
    └── true
        │
        ▼
SentenceImageEditPromptBuilder.BuildPrompt(delta)
    │   "Only edit: box -> apple."
    │
    ▼
IReferenceImageEditClient.EditReferenceAsync(request)
    │   (QwenReferenceImageEditAdapter → IQwenImageReferenceEditClient)
    │
    ▼
AIImageResponse (编辑后的图片)
```

**关键特性**：
- 不依赖旧 `GenerateReferenceEditPromptsAsync` / `GenerateReferenceEditNegativePrompt` / `BuildImageEditContext`
- 不依赖 `MeaningHint` 文本解析
- 不依赖 `SentenceImageReferenceContext`
- 结构化 JSON delta，可审计、可调试
- 编辑失败自动回退到复用源图

## 依赖

| 包 | 用途 |
|---|---|
| `Microsoft.Extensions.Logging.Abstractions` | 日志 |
| `MS.Microservice.AI.Abstractions` | `IAIChatClient`, `IAIImageGenerationClient`, `AIImageGenerationRequest`, etc. |
| `MS.Microservice.AI.Core` | `RoutingAIChatClient`, `RoutingAIImageGenerationClient`, `IAIModelResolver` |

`IPlanGeneratorClient` 的默认实现 `PlanGeneratorClient` 已内置在 Core 项目中。宿主项目无需额外提供实现。
