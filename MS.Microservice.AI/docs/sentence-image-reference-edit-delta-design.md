# Sentence Image Reference Edit Delta Design

## 背景

句子图片生成现在有两类目标：

1. 同一组句子需要保持场景、人物、镜头、画风一致。
2. 每一句又必须只表达当前句子的变化，不能把同组其它句子的物品或动作合并进同一张图。

你指出的关键问题是正确的：如果只把 `referenceImageUrl` 和一个泛化 prompt 交给图片编辑模型，模型并不知道“参考图里上一句具体是什么、当前句子要把哪一部分替换掉”。这样很容易退化成重新文生图，导致人物、场景、布局变化，或者把上一句和当前句子的元素混在一起。

本次改造的核心不是单纯加 prompt，而是让代码在触发 image edit 时携带“参考图语义上下文”，再生成明确的 reference-to-current delta prompt。

## 为什么不默认再加一个读图 agent

直觉上可以这样做：

1. 把参考图 URL 发给一个视觉 LLM。
2. 让它识别图片里有什么。
3. 再把识别结果和当前句子对比。
4. 组装最终图片编辑 prompt。

这个方案是可行的，但它更适合作为“校验/兜底 agent”，不是默认主链路。原因是：

1. 参考图是我们自己生成的，不是外部未知图片。
2. **生成参考图时，我们已经知道它对应的句子、visual focus、visual action、variable elements。**
3. **对于“上一句是 box，当前句是 apple”这种教学图片，真正需要的不是像素级 diff，而是语义级替换指令：把上一句的变量元素替换成当前句的变量元素，同时冻结稳定场景和人物。**
4. 额外视觉 agent 会增加成本、延迟和不确定性；它也可能误读图片，把模型生成的噪声当成必要元素。

所以当前实现采用的是：

> 生成来源可追溯的语义 delta，而不是重新读图做视觉 diff。

也就是说，我们不问“图片里真实像素有什么”，而是利用系统已经掌握的生成来源回答：“这张参考图本来应该表达哪一句；那一句的可变元素是什么；当前句子要替换成什么。”

图片编辑模型本身已经能看到参考图像素。我们的文本 prompt 主要负责告诉它：

1. 哪些东西必须冻结。
2. 参考图当前表达的句子是什么。
3. 当前目标句子是什么。
4. 哪些上一句变量元素要替换成当前句变量元素。

## 当前链路总览

```mermaid
flowchart TD
    A["GenerateSentenceImagesWithSceneContextAsync"] --> B["Load/Create VisualContextGroup"]
    B --> C["Find or create master reference context"]
    C --> D["BuildSceneContext(group, member)"]
    D --> E{"Has reference image?"}
    E -- "No" --> F["Generate first image normally"]
    F --> G["BuildSentenceImageReferenceContext(imageUrl, sentence, member)"]
    E -- "Yes" --> H["BuildImageEditContext(group, reference, currentSentence, member)"]
    H --> I["ApplySentencePromptBranches"]
    I --> J["WordImagePromptPipeline.GeneratePromptsAsync"]
    J --> K["QwenSafePromptBuilder preserves control context"]
    K --> L["ReferenceImageEdit TextToImageWithReferenceAsync"]
    L --> M["Image edit model receives reference image + delta prompt"]
```

## 关键代码说明

### 1. `SentenceImageReferenceContext`


这个模型描述“参考图对应的句子语义”：

```csharp
public sealed record SentenceImageReferenceContext(
    string ImageUrl,
    string SentenceText,
    string? VisualFocus,
    string? VisualAction,
    IReadOnlyList<string> VariableElements);
```

它不是识别图片后得到的内容，而是参考图生成时的来源信息。例如：

```text
ImageUrl: https://...
SentenceText: What a cloudy day!
VisualFocus: a cloudy day
VisualAction: Show a cloudy outdoor scene.
VariableElements: clouds, muted sky
```

当前句子可能是：

```text
SentenceText: What a nice sunny day!
VisualFocus: a nice sunny day
VisualAction: Show a bright, pleasant sunny outdoor scene.
VariableElements: sky brightness, sunny light, foreground landscape
```

这两组结构化信息足够拼出一个明确的语义 delta。

### 2. 批量句子图片服务持有 reference context

> 这部分逻辑位于应用层编排服务中（非 Core 类库），负责协调 prompt pipeline 与图片生成 API 的调用流程，管理组内参考图状态。

之前主流程只保存：

```csharp
string? masterImageUrl
```

这只能告诉后续流程“参考图在哪里”，不能告诉后续流程“参考图表达什么”。

现在改为：

```csharp
SentenceImageReferenceContext? masterReference
```

当组内已经有历史图片时：

```csharp
FindExistingGroupReferenceContext(group, allSentencesById)
```

会从已有 `PictureUrl` 所属句子和 `VisualContextMember` 还原 reference context。

当本轮生成第一张图后：

```csharp
BuildSentenceImageReferenceContext(imageUrl, sentence, member)
```

会把第一张图的 URL、句子、focus、action、variable elements 保存为后续图片编辑的 master reference。

这样后续句子进入 image edit 时，拿到的不再只是 URL，而是：

```text
reference image URL
reference sentence
reference visual focus
reference visual action
reference variable elements
current sentence
current visual focus
current visual action
current variable elements
```

### 3. `BuildImageEditContext` 输出 delta，不再只 freeze

文件：`src/MS.Microservice.AI.Core/Images/Building/SentenceImageContinuityPromptBuilder.cs`

之前 prompt 的核心是：

```text
IMAGE EDIT FREEZE
Use the reference image as the master scene.
Only update the current sentence target...
Allowed changing elements...
```

这仍然太抽象，因为它没有说明“参考图当前是什么”。

现在变为：

```text
IMAGE EDIT DELTA: edit the provided reference image instead of generating a new composition.
Reference image currently illustrates: "What a cloudy day!"
Reference row visual focus: a cloudy day.
Reference row variable elements: clouds, muted sky.
Current target sentence: "What a nice sunny day!"
Current target visual focus: a nice sunny day.
Current row variable elements allowed to change: sky brightness, sunny light, foreground landscape.
Replace or revise only the previous row-specific elements (clouds, muted sky) into the current row-specific elements (sky brightness, sunny light, foreground landscape).
```

这条指令的本质是：

```text
冻结稳定部分 + 说明参考图状态 + 说明当前目标 + 给出变量元素替换关系
```

这比“保持一致，只改当前目标”更接近图片编辑模型需要的操作指令。

### 4. 移除场景污染源

之前 edit prompt 里有一句硬编码：

```text
Do not redesign the classroom...
```

这个在户外、操场、乡村场景里会污染模型注意力，很容易把 classroom、board、blackboard 重新带入画面。

现在改成场景无关的稳定表达：

```text
Do not redesign the stable scene...
```

并且在 `QwenSafePromptBuilder` 透传控制上下文时，会过滤掉 board 相关控制句，避免前面反复出现的黑板问题继续被 prompt 激活。

### 5. `QwenSafePromptBuilder` 保证控制上下文不丢

文件：`src/MS.Microservice.AI.Core/Images/Building/QwenSafePromptBuilder.cs`

这是本次发现的更深一层问题：`imageInput` 不是直接发给图片模型，它会先进入 `WordImagePromptPipeline`，最后由 `QwenSafePromptBuilder` 生成真正发给 Qwen 的 safe prompt。

如果 `QwenSafePromptBuilder` 不保留这些控制信息，那么前面拼好的 `IMAGE EDIT DELTA` 会在中间被重写掉，最后仍然变成泛化文生图 prompt。

所以新增了控制上下文透传：

```text
Sentence image control context: ...
```

它只保留关键控制句，例如：

```text
IMAGE EDIT DELTA
Reference image currently illustrates...
Current target sentence...
Reference row variable elements...
Current row variable elements...
Replace or revise only...
Prompt branch target-object-focus...
red arrow pointer...
Sparse-scene composition...
```

同时会过滤掉明显容易污染画面的负向语言和 board 相关句子。

### 6. 参考图编辑服务使用 image edit 模式

> 这部分逻辑位于应用层编排服务中（非 Core 类库），封装了对第三方图片编辑 API 的调用。

`TextToImageWithReferenceAsync` 会把参考图和文本一起发给多模态生成接口：

```csharp
new MultimodalMessageContent(Image: referenceImageUrl),
new MultimodalMessageContent(Text: editPrompt)
```

并且 wrapper prompt 明确告诉模型：

```text
Treat any IMAGE EDIT DELTA section as the authoritative reference-to-current change list.
```

也就是：最终不是让模型自由重绘，而是让它参考图片，并按 delta 执行局部变化。

## 和“单独视觉 agent 做 diff”的关系

当前方案解决的是主链路的确定性问题：

```text
我知道这张参考图是用哪一句生成的，所以我知道它应该代表什么。
```

视觉 agent 解决的是另一类问题：

```text
我怀疑这张参考图实际生成歪了，所以我要检查图片真实内容。
```

两者不是互斥关系，而是分层关系。

建议的长期架构是：

1. 默认使用当前语义 delta 方案，成本低、速度快、可控性好。
2. 如果图片质量审核失败，或者检测到高风险场景，再触发视觉 agent。
3. 视觉 agent 返回“实际图片内容摘要”和“偏离项”。
4. 再把视觉 agent 的结果合并进下一轮 edit prompt。

例如：

```text
Reference provenance says: sunny playground with one child.
Vision audit says: image also contains blackboard and two extra toys.
Correction delta: remove visual emphasis from board/toys, keep only slide as the environmental anchor.
```

这样视觉 agent 是质量闭环的一部分，而不是每张图的默认成本。

## 当前方案的边界

当前方案不能保证识别参考图真实像素内容。

如果模型生成第一张参考图时已经严重跑偏，例如：

1. 多了黑板。
2. 人物身份已经变了。
3. 画面出现中文。
4. 实际主体不是 prompt 里的主体。

那么 `SentenceImageReferenceContext` 不会自动知道这些偏差，因为它记录的是“生成意图”，不是“视觉识别结果”。

所以它适合解决：

```text
后续 edit 不知道上一张图代表哪一句，因此无法做语义替换。
```

它不完全解决：

```text
上一张图自身已经生成失败，需要读图发现失败点。
```

后一类问题应该由视觉审核 agent 或图片质量检测流程补上。

## 测试覆盖

新增/调整的测试集中在：

文件：`test/MS.Microservice.AI.Core.Tests/Images/QwenSafePromptBuilderTests.cs`

覆盖点：

1. image edit prompt 会包含 reference sentence、current sentence、previous variable elements、current variable elements。
2. 户外场景不会再注入 classroom。
3. `IMAGE EDIT DELTA` 会穿透到最终 Qwen safe prompt。
4. `target-object-focus` 和 `red arrow pointer` 分支会穿透到最终 Qwen safe prompt。

验证命令：

```powershell
dotnet test test/MS.Microservice.AI.Core.Tests/MS.Microservice.AI.Core.Tests.csproj
dotnet build src/MS.Microservice.AI.Core/MS.Microservice.AI.Core.csproj
```

当前验证结果：

1. 单元测试通过：34/34。
2. Web 项目 build 通过。
3. build 仍有原有 `Microsoft.OpenApi` NU1903 包漏洞 warning，不是本次改造引入。

## 一句话总结

这次改造的原理是：利用我们已经拥有的句子分组和生成来源信息，把 image edit 从“参考图 + 泛化 prompt”改成“参考图 + 明确语义 delta”。它不是视觉读图 diff，而是 provenance-based semantic diff；视觉 agent 可以作为后续审核和兜底层加入。
