# Fz.ChatGPT.ImageGeneration

英语教学卡片图片 Prompt 生成类库。将原始文本输入（单词、短语、句子）转换为高质量的生图 prompt，支持两路输出：

- **Rich prompt** — 含完整约束、否定词、语义分析，存入数据库用于追溯
- **Safe prompt** — 纯正向、零敏感词，直接发送给 Qwen/DashScope 文生图 API

---

## 架构

```
                          ┌─────────────────────────┐
                          │   Fz.ChatGPT.Web        │
                          │  (ResourceGeneration    │
                          │   Service, Controllers) │
                          └───────────┬─────────────┘
                                      │ 依赖注入
                          ┌───────────▼─────────────┐
                          │   PlanGeneratorClient   │ ← 实现 IPlanGeneratorClient
                          │   (封装 AzureChatService) │
                          └───────────┬─────────────┘
                                      │
┌─────────────────────────────────────┼─────────────────────────────┐
│  Fz.ChatGPT.ImageGeneration         │                             │
│                                     ▼                             │
│  ┌──────────────────────────────────────────┐                    │
│  │        WordImagePromptPipeline            │ ← 主编排器        │
│  │  GeneratePromptsAsync(wordText) → (rich,  │                    │
│  │                              safe)         │                    │
│  └──────────────┬───────────────────────────┘                    │
│                 │                                                │
│    ┌────────────┼────────────┐                                   │
│    ▼            ▼            ▼                                   │
│  Parse      PlanGen      Build                                    │
│  ─────      ───────      ─────                                    │
│  Raw text   IPlanGen-    EducationalFlashcardPromptBuilder        │
│  → Input    eratorClient  QwenSafePromptBuilder                   │
│             .Generate-                                            │
│             VisualPlan-                                           │
│             Async()                                               │
│                 │                                                 │
│    ┌────────────┼────────────┐                                   │
│    ▼            ▼            ▼                                   │
│  Enrich      Validate     Repair                                  │
│  ───────     ────────     ──────                                  │
│  确定性规则   语义校验     自动修复                                 │
│  补强 LLM     (禁止句/     (补注入缺                                 │
│  输出        安全句/      失的 required                             │
│              教室/跑步)   action /                                 │
│                          warning cue                               │
│                          / safety cue                              │
│                          / classroom)                              │
└──────────────────────────────────────────────────────────────────┘
```

## 项目结构

```
Fz.ChatGPT.ImageGeneration/
├── Models/                              # 纯数据模型，零外部依赖
│   ├── WordImageCardType.cs             # 卡片类型常量 (alphabet/word/phrase/sentence/abstract)
│   ├── WordImageInput.cs                # 解析后的输入
│   ├── WordImagePromptPlan.cs           # 最终合并计划 (LLM + 规则补强 + 校验修复)
│   └── WordImageVisualPlan.cs           # LLM 原始输出的结构化视觉计划
│
├── IPlanGeneratorClient.cs              # LLM 调用抽象接口
│
├── WordImagePromptPipeline.cs           # 主编排器
│   ├── GeneratePromptsAsync()           # 主入口 → (richPrompt, safePrompt)
│   ├── GenerateSafePromptAsync()        # 仅返回 safe prompt
│   ├── GenerateRichPromptAsync()        # 仅返回 rich prompt
│   ├── Parse()                          # 文本解析 → WordImageInput
│   └── InferCardType()                  # 卡片类型推断
│
├── Pipeline/                            # 计划处理管线
│   ├── VisualPlanEnricher.cs            # 确定性规则补强 (禁止句/安全句/教室/跑步)
│   ├── VisualPlanValidator.cs           # 语义完整性校验
│   └── VisualPlanRepairer.cs            # 校验失败自动修复
│
├── Building/                            # Prompt 组装
│   ├── EducationalFlashcardPromptBuilder.cs  # Rich prompt 构建
│   ├── QwenSafePromptBuilder.cs              # Safe prompt 构建 (纯正向)
│   └── SentenceSemanticRulesProvider.cs      # 句子级语义规则注入
│
├── Analysis/                            # 语义分析
│   └── SentenceSemanticAnalyzer.cs      # 正则分析 (禁止句/安全句/教室/跑步)
│
└── Helpers/                             # 工具类
    ├── PromptSanitizer.cs               # 负向词 & 敏感词清洗 (防 Qwen 内容审核拦截)
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
MergeVisualPlan() → WordImagePromptPlan  ← 合并 LLM 输出 + 规则补强 + 修复结果
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

## 宿主项目集成

```csharp
// Program.cs — DI 注册
builder.Services.AddTransient<WordImagePromptPipeline>();
builder.Services.AddTransient<IPlanGeneratorClient, PlanGeneratorClient>();

// ResourceGenerationService.cs — 使用
public class ResourceGenerationService(..., WordImagePromptPipeline pipeline)
{
    public async ValueTask<string?> GenerateSafeImagePromptAsync(string content)
        => await pipeline.GenerateSafePromptAsync(content);

    public async ValueTask<(string, string)> GenerateImageCoreAsync(string content, string path)
    {
        var (rich, safe) = await pipeline.GeneratePromptsAsync(content);
        var imageUrl = await dashScopeService.TextToImageAsync(safe!);
        // ... upload, resize ...
        return (imageCdnUrl, rich!);
    }
}
```

## 关键设计决策

### 1. 为什么 Safe Prompt 不能简单去掉否定词

Qwen/DashScope 的内容审核是基于 prompt **文本本身**做关键词扫描的，不看语义意图。以下 prompt 仍然会被拦截：

```
Do not show any violence, blood, or weapons.
```

因为 `violence`, `blood`, `weapons` 三个词出现在了文本中，即使前面有 "Do not show"。

`QwenSafePromptBuilder` 使用 `PromptSanitizer` 完全剔除所有敏感词，并用 `CleanNegativeLanguage` 将否定约束转换为正向描述。

### 2. 为什么 LLM Planner + 确定性规则

LLM 规划的视觉计划可能遗漏关键语义元素（例如忘记画禁止动作、忘记画制止线索）。`VisualPlanEnricher` 用正则表达式检测句子类型，强制注入缺失的 `mustShow`/`mustNotShow` 约束。`VisualPlanValidator` 在 prompt 组装前做最终检查，`VisualPlanRepairer` 自动修复问题。

### 3. 为什么需要 IPlanGeneratorClient 抽象

将 LLM 调用抽象为接口的好处：
- 类库不依赖 `Azure.AI.OpenAI` / `OpenAI.Chat` 等重型包
- 可替换 LLM 提供商（Azure OpenAI → 其他）
- 单元测试可用 mock 实现

## 依赖

| 包 | 用途 |
|---|---|
| `Microsoft.Extensions.Logging.Abstractions` | 日志 |
| `Newtonsoft.Json` | JSON 序列化 |

宿主项目 (`Fz.ChatGPT.Web`) 额外需要提供 `IPlanGeneratorClient` 的实现。
