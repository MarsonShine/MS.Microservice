# MS.Microservice.AI.QuestionGeneration Code Guide

`MS.Microservice.AI.QuestionGeneration` 是一个 provider-neutral 的题目生成 Harness。它把题目生成从“一次 Prompt 调用”拆成有边界、可审计、可扩展的质量循环：

```text
Frozen Context + Blueprint
        ↓
      Draft
        ↓
Deterministic Validation
        ↓
Independent Review
        ↓
Targeted Repair ──┐
        ↑          │
        └──────────┘
        ↓
Duplicate Gate → Accepted
```

深入理解状态机和故障语义，请阅读 [架构深入解析](docs/ARCHITECTURE.md)。

## 模块边界

模块负责：

- 冻结 Context 与 Blueprint 身份检查。
- Draft、确定性校验、独立 Review、定向 Repair。
- 调用、Token、成本、Repair 和格式重发预算。
- Repair 字段 allowlist 和不可变字段保护。
- Attempt 记录、Invocation Reservation 和停止原因。
- 精确指纹去重，以及可替换的重复检测接口。
- 通过现有 `IAIChatClient` 使用 JSON Schema、JSON Object 或严格 JSON。

模块不负责：

- 业务题型和业务题型编号。
- 从数据库构建 Context。
- HTTP API、后台任务、租约、检查点或分布式恢复。
- 题目持久化、旧表投影、人工审核。
- TTS、图片、对象存储或资源 Outbox。
- API Key；密钥仍由框架的 `IAISecretProvider` 解析。

## 推荐阅读顺序

1. `Contracts/QuestionContracts.cs`：Context、Blueprint、Candidate 和类型标识。
2. `Contracts/ExtensionContracts.cs`：Definition、Planner、Prompt 和去重扩展点。
3. `Contracts/ModelContracts.cs`：Draft、Review、Repair 的模型协议。
4. `Pipeline/HarnessContracts.cs`：预算、Attempt、停止原因和结果。
5. `Pipeline/QuestionGenerationHarness.cs`：完整控制循环。
6. `Serialization/SystemTextJsonQuestionContract.cs`：Schema 与严格 JSON。
7. `AIChat/AIChatQuestionModelClient.cs`：现有 AI 网关适配及格式降级。
8. `DependencyInjection/QuestionGenerationServiceCollectionExtensions.cs`：DI 外观和生命周期。

## 目录职责

```text
MS.Microservice.AI.QuestionGeneration/
├─ AIChat/              IAIChatClient 适配、模型场景、格式能力缓存
├─ Contracts/           公共领域契约和宿主扩展点
├─ DependencyInjection/ DI 注册入口
├─ Pipeline/            Harness、预算、Attempt、去重
├─ Prompts/             默认的安全角色规范
├─ Serialization/       Strict JSON 和运行时 JSON Schema
└─ docs/                架构深入解析
```

## 注册

先注册 AI 框架和所需 Provider，再注册 QuestionGeneration：

```csharp
builder.Services
    .AddMicroserviceAI(builder.Configuration)
    .AddOpenAI();

builder.Services
    .AddQuestionGeneration(options =>
    {
        options.DraftScenario = "QuestionGenerationDraft";
        options.ReviewScenario = "QuestionGenerationReview";
        options.RepairScenario = "QuestionGenerationRepair";
    })
    .AddDefinition<ShortAnswerDefinition>();
```

三个场景仍配置在框架统一的 `AI:Models:Chat` 下。QuestionGeneration 不配置 Provider 地址或密钥。

## 一个完整的自定义题型

### 1. Candidate

```csharp
public sealed record ShortAnswerCandidate : QuestionCandidate
{
    public required string Stem { get; init; }
    public required string Answer { get; init; }
}
```

### 2. Definition

```csharp
public sealed class ShortAnswerDefinition : IQuestionDefinition
{
    public QuestionTypeId QuestionType { get; } = new("short-answer");
    public Type CandidateType => typeof(ShortAnswerCandidate);

    public QuestionReviewRubric Rubric { get; } =
        new(["correctness", "grounding"], MinimumAverageScore: 80, MinimumDimensionScore: 70);

    // JSON 顶层字段名。Repair 永远不能修改这些字段。
    public IReadOnlySet<string> ImmutableFields { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "answer" };

    public QuestionEligibilityResult CheckEligibility(
        QuestionContextSnapshot context,
        QuestionBlueprint blueprint) =>
        QuestionEligibilityResult.Eligible;

    public QuestionValidationResult Validate(
        QuestionCandidate candidate,
        QuestionBlueprint blueprint,
        QuestionContextSnapshot context)
    {
        var item = (ShortAnswerCandidate)candidate;
        return string.IsNullOrWhiteSpace(item.Stem)
            ? new([
                new(
                    "stem_required",
                    QuestionIssueSeverity.Error,
                    "stem",
                    "Stem is required.",
                    Repairable: true)
              ])
            : QuestionValidationResult.Success;
    }

    public string BuildComparableText(QuestionCandidate candidate)
    {
        var item = (ShortAnswerCandidate)candidate;
        return $"{item.Stem} {item.Answer}";
    }
}
```

`QuestionValidationIssue.Field` 必须使用 Candidate 的 JSON 字段路径。Harness 取第一个路径段构造 Repair allowlist，例如 `options[0].text` 会允许修改 `options`。

### 3. Planner

具体来源选择、配额和业务规则属于宿主：

```csharp
public sealed class ShortAnswerPlanner : IQuestionBlueprintPlanner
{
    public ValueTask<QuestionBlueprintPlan> PlanAsync(
        QuestionPlanningRequest request,
        QuestionContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        var blueprint = new QuestionBlueprint
        {
            BlueprintId = StableId(request.TaskKey, context.Hash, "short-answer", 1),
            QuestionType = new("short-answer"),
            Sequence = 1,
            ContextVersion = context.Version,
            ContextHash = context.Hash,
            SpecificationVersion = "short-answer-v1",
            Constraints = JsonSerializer.SerializeToElement(new { maxWords = 20 }),
        };

        return ValueTask.FromResult(new QuestionBlueprintPlan([blueprint], []));
    }
}
```

Planner 必须是确定性的。相同请求、Context Hash、版本和序号必须产生相同 Blueprint。

### 4. Prompt Provider

默认 Prompt 只提供安全的数据边界和三个角色的通用职责。业务题型通常应替换它：

```csharp
public sealed class ApplicationQuestionPrompts : IQuestionPromptProvider
{
    public QuestionPromptSpecification Get(
        QuestionTypeId type,
        QuestionPromptStage stage) =>
        new(
            $"application/{type}/{stage}/v1",
            "v1",
            LoadVersionedInstructions(type, stage),
            $"{type.Value.Replace('-', '_')}_{stage.ToString().ToLowerInvariant()}_v1");
}

services.AddSingleton<IQuestionPromptProvider, ApplicationQuestionPrompts>();
```

业务规则不能只存在于 Prompt。可以确定的规则必须在 `IQuestionDefinition.Validate` 中再次表达。

## 构建 Context 与运行 Harness

```csharp
var context = new QuestionContextSnapshot
{
    ContextId = "unit-100",
    Version = "snapshot-v1",
    Hash = computedStableHash,
    Data = JsonSerializer.SerializeToElement(new
    {
        topic = "fractions",
        sourceTexts = sourceTexts,
        learningObjectives = objectives,
    }),
    ExistingQuestions = existingQuestionReferences,
};

var harness = serviceProvider.GetRequiredService<IQuestionGenerationHarness>();
var result = await harness.RunAsync(
    context,
    blueprint,
    QuestionGenerationBudget.Balanced,
    attemptObserver,
    cancellationToken);

if (result.Accepted)
{
    // 1. 在宿主事务中持久化 result.Candidate、来源关系和业务任务状态。
    await repository.SaveAsync(result.Candidate!, cancellationToken);

    // 2. 事务确认成功后，才提交进程内批次账本。
    harness.CommitAccepted(result);
}
```

`Accepted` 只表示候选通过质量门，不表示数据库已保存。

## Attempt Observer

需要崩溃安全审计的宿主可以实现：

```csharp
public sealed class DurableAttemptObserver : IGenerationAttemptObserver
{
    public ValueTask OnInvocationStartingAsync(
        QuestionGenerationInvocation invocation,
        CancellationToken cancellationToken)
    {
        // 在模型调用之前预留 InvocationId。
        return store.ReserveAsync(invocation, cancellationToken);
    }

    public ValueTask OnAttemptRecordedAsync(
        QuestionGenerationAttempt attempt,
        CancellationToken cancellationToken)
    {
        // 模型调用结束后追加结果，不覆盖历史 Attempt。
        return store.AppendAsync(attempt, cancellationToken);
    }
}
```

模块不会提供数据库实现。宿主恢复时若发现只有 Reservation、没有完成 Attempt，应 fail closed，不能盲目重放可能已经发生的模型调用。

## 模型场景配置

非敏感配置示例：

```json
{
  "AI": {
    "Models": {
      "Chat": {
        "QuestionGenerationDraft": {
          "Provider": "OpenAI",
          "Model": "draft-model",
          "Temperature": 0.4,
          "MaxOutputTokens": 3000
        },
        "QuestionGenerationReview": {
          "Provider": "OpenAI",
          "Model": "review-model",
          "Temperature": 0,
          "MaxOutputTokens": 1800
        },
        "QuestionGenerationRepair": {
          "Provider": "OpenAI",
          "Model": "repair-model",
          "Temperature": 0.2,
          "MaxOutputTokens": 3000
        }
      }
    }
  }
}
```

密钥只使用环境变量：

```text
AI__PROVIDERS__OpenAI__ApiKey
AI__PROVIDERS__DeepSeek__ApiKey
AI__PROVIDERS__Qwen__ApiKey
```

也可以在 Provider 的 `ApiKeySecretName` 指定自定义环境变量名。不要在 `appsettings.json`、Prompt、Context 或日志中保存密钥。

## 结构化输出

`AIChatQuestionModelClient` 的顺序为：

1. `JsonSchema`
2. Provider 明确返回 `unsupported_response_format` 时降级为 `JsonObject`
3. 再次明确不支持时降级为 `Text`
4. 所有响应最终经过本地严格 JSON 反序列化

能力按 Provider+Model 缓存。认证、权限、限流、内容安全、超时和普通参数错误不会触发格式降级。

严格反序列化拒绝：

- Markdown 代码围栏。
- 根 JSON 后的尾随内容。
- 注释和尾随逗号。
- 未声明字段。
- 空结果和不匹配的 Candidate 类型。

## 服务生命周期

| 服务 | 生命周期 | 原因 |
|---|---|---|
| `IQuestionModelClient` | Singleton | 复用 Provider+Model 格式能力缓存 |
| `IQuestionJsonContract` | Singleton | 复用 Schema 缓存和只读序列化配置 |
| `QuestionDefinitionRegistry` | Singleton | Definition 是无状态规则 |
| `IQuestionPromptProvider` | Singleton | Prompt 规范必须版本稳定 |
| `IQuestionDuplicateDetector` | Singleton | 默认实现无状态 |
| `IQuestionGenerationHarness` | Transient | 每次解析拥有独立批次去重账本 |

不要跨无关批次长期复用同一个 Harness。

## 增加题型的检查清单

1. 定义稳定的 `QuestionTypeId`。
2. 定义一个 `QuestionCandidate` 派生类型。
3. 实现 `IQuestionDefinition`。
4. 指定不可变字段和 Review Rubric。
5. 在 Planner 中构建确定性 Blueprint。
6. 提供版本化 Draft、Review、Repair Prompt。
7. 通过 `AddDefinition<T>()` 注册。
8. 增加资格、验证、Repair allowlist、Schema、Review 和去重测试。
9. 修改契约时升级 Schema/Specification/RuleSet/Rubric 版本。
10. 使用冻结评测集比较新旧版本，不直接全量切换。

## 常见停止原因

| StopReason | 含义 |
|---|---|
| `Ineligible` | Context 身份不匹配或 Definition 判定不可生成 |
| `DraftFailed` | Draft Provider 调用失败 |
| `InvalidStructuredOutput` | 拒绝、截断或严格 JSON 失败 |
| `ValidationFailed` | 存在不可修复确定性问题或 Repair 越权 |
| `ReviewRejected` | Reviewer 明确拒绝 |
| `RepairExhausted` | 达到 Repair 上限 |
| `NoProgress` | 候选和问题签名未发生有效变化 |
| `BudgetExceeded` | 调用、Token 或成本预算超限 |
| `DuplicateDetected` | 命中已有题或已提交的批次题 |

## 测试

```powershell
dotnet test MS.Microservice.AI/test/MS.Microservice.AI.QuestionGeneration.Tests/MS.Microservice.AI.QuestionGeneration.Tests.csproj
dotnet build MS.Microservice.AI/MS.Microservice.AI.slnx
git diff --check
```

测试必须使用 Fake Model/Provider，不调用真实外部模型。
