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
        "ApiKey": "<from-user-secrets>",
        "BaseAddress": "https://api.openai.com/v1/",
        "TimeoutSeconds": 120
      },
      "Qwen": {
        "ApiKey": "<from-user-secrets>",
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
        "ApiKey": "<secret>",
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

### Scenario 说明

| Scenario | Capability | 用途 |
|---|---|---|
| `ImagePromptPlanning` | Chat | LLM 视觉规划（可自定义，传入 `AddImagePromptPipeline("MyScenario")`) |
| `Default` | ImageGeneration | 实际图片生成（可通过 `AIImageGenerationRequest.Scenario` 覆盖） |

---

## 项目结构

```
Images/
├── Models/                              # 纯数据模型
│   ├── WordImageCardType.cs             # 卡片类型常量
│   ├── WordImageInput.cs                # 解析后的输入
│   ├── WordImageRow.cs                  # 批量输入行（Excel/DB）
│   ├── WordImagePromptPlan.cs           # 最终合并计划
│   ├── WordImageVisualPlan.cs           # LLM 原始输出的结构化视觉计划
│   ├── WordImagePromptResult.cs         # 批量生成结果
│   ├── VisualContextGroup.cs            # 视觉上下文组
│   ├── VisualContextMember.cs           # 组成员（行级视觉指引）
│   ├── CharacterProfile.cs              # 角色外观画像
│   ├── SceneGroup.cs                    # 场景组（旧版）
│   ├── SceneContextPlan.cs              # 场景上下文计划
│   ├── SentenceImageGenerationMode.cs   # 批量生成模式枚举
│   └── SentenceImageReferenceContext.cs # 参考图句子内容描述（IMAGE EDIT DELTA 输入）
│
├── IPlanGeneratorClient.cs              # LLM 视觉规划抽象接口
├── ISceneGroupingAgent.cs               # 场景分组抽象接口（保留以备未来扩展）
├── PlanGeneratorClient.cs               # 默认 LLM 规划实现（使用 IAIChatClient + scenario）
├── SceneGroupingAgent.cs                # 场景分组实现（直接注册为 Singleton）
├── WordImagePromptPipeline.cs           # 主编排器：Parse → Plan → Enrich → Build
├── ImageGenerationOrchestrator.cs       # 一站式编排器：文本 → prompt → 图片
│
├── Pipeline/                            # 计划处理管线
│   ├── VisualPlanEnricher.cs            # 确定性规则补强（MustShow/MustNotShow 注入，SettingCues 归一化为 1）
│   ├── VisualPlanValidator.cs           # 语义完整性校验
│   ├── VisualPlanRepairer.cs            # 校验失败自动修复（SettingCues 归一化为 1）
│   └── VisualPlanSceneSimplifier.cs     # 场景去冗余：剔除教室关键词、杂乱关键词，仅保留 1 个核心环境锚点
│
├── Building/                            # Prompt 组装
│   ├── EducationalFlashcardPromptBuilder.cs  # Rich prompt（含反冗余约束）
│   ├── QwenSafePromptBuilder.cs              # Safe prompt（settingCues 上限 2 个；支持 SentenceImageControlContext 注入）
│   ├── SentenceSemanticRulesProvider.cs      # 句子级语义规则
│   ├── SentenceImageContinuityPromptBuilder.cs # 连续场景 prompt 构建（IMAGE EDIT DELTA 双上下文：Reference + Current Target）
│   ├── SentenceImagePromptBranchComposer.cs   # 特殊场景分支注入
│   └── SentenceImageReferenceEditPolicy.cs    # 参考图编辑策略
│
├── Analysis/                            # 语义分析
│   └── SentenceSemanticAnalyzer.cs      # 正则分析（禁止/安全/教室/跑步检测）
│
└── Helpers/                             # 工具类
    ├── PromptSanitizer.cs               # 负向词 & 敏感词清洗
    └── PromptNormalizer.cs              # 值/场景/文本规范化
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

### 方式一：一站式编排器（推荐）

使用 `ImageGenerationOrchestrator` 一步完成"文本 → prompt → 图片"的完整流程：

```csharp
// Program.cs — DI 注册
builder.Services.AddMicroserviceAI(builder.Configuration)
    .AddOpenAI()        // 或其他 Provider
    .Services
    .AddImagePromptPipeline();  // 注册 prompt pipeline + orchestrator

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
        // 一步到位：文本 → prompt 规划 → 图片生成
        var result = await orchestrator.GenerateFromTextAsync(wordText);

        // result.RichPrompt  → 存入数据库用于追溯
        // result.SafePrompt  → 实际发送给图片 API 的 prompt
        // result.ImageResponse.Images → 生成的图片列表
        return result;
    }

    // 仅生成 prompt（不调用图片 API）
    public async Task<(string?, string?)> PreviewPromptAsync(string wordText)
    {
        return await orchestrator.GeneratePromptsOnlyAsync(wordText);
    }
}
```

### 方式二：分步调用

直接使用 `WordImagePromptPipeline`，自行控制图片生成：

```csharp
public class ResourceGenerationService
{
    private readonly WordImagePromptPipeline pipeline;
    private readonly IAIImageGenerationClient imageClient;

    public ResourceGenerationService(
        WordImagePromptPipeline pipeline,
        IAIImageGenerationClient imageClient)
    {
        this.pipeline = pipeline;
        this.imageClient = imageClient;
    }

    public async ValueTask<string?> GenerateSafeImagePromptAsync(string content)
        => await pipeline.GenerateSafePromptAsync(content);

    public async ValueTask<(string?, string?)> GenerateImageCoreAsync(string content)
    {
        var (rich, safe) = await pipeline.GeneratePromptsAsync(content);
        var imageResponse = await imageClient.GenerateAsync(new AIImageGenerationRequest
        {
            Prompt = safe!,
            Size = "1024x1024",
        });
        // ... upload, resize ...
        return (imageResponse.Images.FirstOrDefault()?.Url, rich);
    }
}
```

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
    ├── object_drill / dialogue / greeting → 使用参考图编辑保持连续性
    ├── safety_rules / instructional_sequence → 各自独立生成
    └── 组内句子 → SentenceImageContinuityPromptBuilder 注入共享上下文
```

**配置方式**：`AddImagePromptPipeline()` 自动注册 `SceneGroupingAgent` 为 Singleton。

```csharp
// 使用 SceneGroupingAgent
var agent = provider.GetRequiredService<SceneGroupingAgent>();
var result = await agent.GroupAsync(rows);
// result.Groups → 分组结果
// result.UncertainRowIds → 需要人工审核的行
```

### 8. 组件接口概览

| 类型 | 生命周期 | 职责 |
|---|---|---|
| `IPlanGeneratorClient` → `PlanGeneratorClient` | Singleton | LLM 视觉规划（alphabet / visual plan） |
| `SceneGroupingAgent` | Singleton | LLM 句子场景分组（直接注册为具体类） |
| `WordImagePromptPipeline` | Transient | 文本解析 → 视觉规划 → prompt 构建 |
| `ImageGenerationOrchestrator` | Transient | 一站式：文本 → prompt → 图片 |

### 9. SentenceImageControlContext / IMAGE EDIT DELTA

当 `WordImageInput.MeaningHint` 包含 `IMAGE EDIT DELTA` / `Current sentence image only` / `Prompt branch` / `Shared context type` 等标记时，`QwenSafePromptBuilder` 会自动解析并注入 `Sentence image control context:` 片段：

- **触发条件**：MeaningHint 不含标记 → 无操作
- **过滤规则**：剔除 Do not / Don't / No / Never / Without 等否定句式；剔除 board/blackboard/whiteboard 引用
- **长度限制**：上下文片段最长 1400 字符；最多 18 个子句
- **适用卡片**：Sentence、Phrase、Word（Alphabet 除外）

> 这里 `SentenceImageContinuityPromptBuilder.BuildImageEditContext` 新增了 **双上下文** 重载：同时传入 `SentenceImageReferenceContext`（参考图）和 current sentence（当前句），生成 "Replace or revise only the previous row-specific elements into the current row-specific elements" 级别的精确编辑指令。

## 依赖

| 包 | 用途 |
|---|---|
| `Microsoft.Extensions.Logging.Abstractions` | 日志 |
| `MS.Microservice.AI.Abstractions` | `IAIChatClient`, `IAIImageGenerationClient`, `AIImageGenerationRequest`, etc. |
| `MS.Microservice.AI.Core` | `RoutingAIChatClient`, `RoutingAIImageGenerationClient`, `IAIModelResolver` |

`IPlanGeneratorClient` 的默认实现 `PlanGeneratorClient` 已内置在 Core 项目中。宿主项目无需额外提供实现。
