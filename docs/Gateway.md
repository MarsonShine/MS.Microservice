# 网关(Gateway)

封装对外部系统或资源的访问的对象

![](https://martinfowler.com/articles/gateway-pattern/gateway.png)

有趣的是软件几乎很少单独存在。团队编写软件的时候通常会与外部系统交互，也许是类库、远程调用外部服务、与数据库交互或是共享文件。通常会为外部系统形成一个API，但是这个 API 经常会显得很尴尬。API 可能使用不同类型，要求需要奇怪的参数，并且以在我们的上下文中没有意义的方式组合字段。处理这样的 API 在使用时可能会导致不匹配。

一个网关的行为就好比作为一个单点入口来面对这些外来者。任何在我们系统中与网关接口交互的代码，都是按照我们系统使用的术语来设计工作的。**然后网关将这个 API 转换为外来者提供的 API**。

虽然（While）这个模式使用得非常广泛（但其实更应该普遍），但“网关”这个名称并没有流行（caught on）起来。因此，尽管您应该期待经常看到这种模式，但它并没有广泛使用这个名称。

## 如何工作

每当我访问一些外部软件时，我都会使用一个网关，而这种外部因素会让我感到尴尬（awkwardness）。我没有让这种尴尬传遍我的代码，而是将其包含到网关中的一个地方。

通过允许测试工具终止网关的连接对象，使用网关能让系统更易测试。访问远程服务，这对于网关来说非常重要，它能根据需要移除慢调用。对于需要为测试提供固定数据但又不是为此设计这样做的外部系统来说，这是至关重要的。我将在这里使用网关，即使外部API 在其他方面是 OK 的（在这种情况下，网关将仅是连接对象）。

网关的另一个优点是，**将一个外部系统替换为另一个外部系统**要容易得多，也经常会发生。类似于在外部系统中更改 API 或更改返回的数据，网关能使我们的代码更易调整，因为任何更改都只影响单个地方。但是尽管有这些好处，这几乎不是使用网关的理由，因为仅仅封装外部 API 就能满足了。

网关的一个关键目的就是转换外来词汇（vocabulary），否则会使主机代码（host code）复杂化。但在做之前，我们需要考虑清楚是否需要使用这些外来词汇。我遇到过这样的情况，一个团队将一个广泛理解的外国词汇翻译成一个特定的代码库词汇，因为“他们不喜欢这些名字”。对于这个决定，我没有一个通用的规则可以陈述，团队必须判断他们是应该采用外部词汇表还是开发自己的词汇表。（在[领域驱动设计模式](https://martinfowler.com/bliki/DomainDrivenDesign.html)中，这是在顺从层（Conformist）和反腐败层（Anticorruption）之间的选择。）

一个特别的例子是，我们在平台上构建，并考虑是否希望将自己与底层平台隔离。在许多情况下，平台的功能非常普遍，不值得进行包装。例如，我不会考虑包装语言的集合类。在这种情况下，我只是接受他们的词汇表作为我的软件词汇表的一部分。

## 延申阅读

我原来在 [P of EAA](https://martinfowler.com/books/eaa.html) 的[这个模式](https://martinfowler.com/eaaCatalog/gateway.html)中描述过。那时，我在考虑是否创造（coin）一个新的模式名称，而不是参考现有的“四人帮”模式：Facade、Adapter 以及 Mediator。最后我决定使用了一个不同的名称，这是值得的。

Facade 仅仅只是将一个复杂的 API 简单化，它通常是由服务的作者为一般用途而做的。网关是由客户端为其特定用途而编写的。

Adapter 是最接近网关的的 Gof 模式，它改变一个类的接口来匹配另一个。但是 adapter 是在已经存在的两个接口的上下文中定义的，而对于网关，我在包装外部元素时定义网关的接口。这种区别使我将gateway视为一个单独的模式。随着时间的推移，人们对“adapter”的使用越来越宽松，因此将网关称为适配器并不罕见。

Mediator 将多个对象分开，以让他们无需知道彼此的存在，它们只需要知道 mediator。对于网关，通常只有一个资源被封装在网关之后，并且该资源不会知道网关。

网关的概念非常适合[领域驱动设计](https://martinfowler.com/bliki/DomainDrivenDesign.html)的[边界上下文](https://martinfowler.com/bliki/BoundedContext.html)。当我在两个不同上下文中使用网关处理时，网关就负责处理外部上下文和我自己上下文中的转换。网关时实现一个反腐层的的方式。因此，一些团队会使用这个术语，用缩写形式“ACL”来命名他们的网关。

术语“网关”的常见用法是 [API 网关](https://microservices.io/patterns/apigateway.html)。根据我在上面概述的原则，这实际上更像是一个 facade，因为它是由服务提供者为一般客户端使用而构建的。

## 例子：简单函数（Typescript）

考虑一个假想的医院应用程序，它监视一系列治疗程序。许多治疗项目都需要预约病人使用骨融合机。为此，应用程序需要与医院的设备预约服务交互。应用程序通过一个库与服务交互，该库公开了一个列出某些设备可用预订槽的功能：

```typescript
// equipmentBookingService.ts
export function listAvailableSlots(equipmentCode: string, duration: number, isEmergency: boolean) : Slot[]
```

因为应用程序只与骨融合机交互，而且从不在紧急情况下使用，所以简化这个函数调用是有意义的。一个简单的网关在这里就可以是一个函数，其命名方式对当前应用程序有意义。

```typescript
export function listBoneFusionSlots(length: Duration) {
    return ebs.listAvailableSlots("BFSN", length.toMinutes(), false)
      .map(convertSlot)
}
```

这个网关功能正在做一些有用的事情。首先，它的名称将其与应用程序中的特定用法联系起来，允许许多调用者包含更清晰地阅读的代码。

网关功能封装了设备预订服务的设备代码。只有这个功能需要知道一个骨融合机，你需要代码“BFSN”。

网关函数将从应用程序内使用的类型转到到 API 使用的类型。在这种情况下，应用程序使用 [js-joda](https://js-joda.github.io/js-joda/) 来处理时间——这是一个常用且明智的选择，可以用 JavaScript 简化任何类型的日期/时间工作。但是，该 API 使用整数表示分钟。网关函数允许调用者在应用程序中使用约定，而不需要考虑如何转换为外部 API 的约定。 

来自应用程序的所有请求都是非紧急的，因此网关不会公开一个总是相同值的参数

最后，使用转换函数从设备预订服务的上下文中转换 API 的返回值。

设备预订服务返回像下面这样的槽对象：

```typescript
export interface Slot {
    duration: number,
    equipmentCode: string,
    date: string,
    time: string,
    equipmentID: string,
    emergencyOnly: boolean,
}
```

但是调用应用程序发现这样的插槽更有用：

```typescript
export interface Slot {
    date: LocalDate,
    time: LocalTime,
    duration: Duration,
    model: EquipmentModel
}
```

所以代码按照约束就是下面这样：

```typescript
// boneFusionGateway.ts
function convertSlot(slot:ebs.Slot) : Slot {
    return {
      date: LocalDate.parse(slot.date),
      time: LocalTime.parse(slot.time),
      duration: Duration.ofMinutes(slot.duration),
      model: modelFor(slot.equipmentID),
    }
}
```

这种转换忽略了对治疗计划应用程序没有意义的字段。它将日期和时间字符串转换为 js-joda。治疗计划用户不关心设备 id 代码，但他们关心槽中可用的设备型号。因此，convertSlot 从其本地存储中查找设备模型，并使用模型记录丰富插槽数据。

通过这样做，治疗计划应用程序不必处理设备预订服务的语言。它可以假装设备预订服务在治疗计划的世界中无缝地工作。

## 例子：使用一个可替换的连接（TypeScript）

网关是通向外部代码的路径，外部代码通常是通向驻留在其他地方的重要数据的路径。这样的外来数据会使测试复杂化。我们不想每次治疗应用程序的开发人员运行我们的测试时都要预订设备插槽。即使该服务提供了一个测试实例，远程调用的缓慢速度经常会削弱快速测试套件的可用性。这时使用 [Test Double](https://martinfowler.com/bliki/TestDouble.html) 是有意义的。

当使用远程服务时，网关履行两个职责。与本地网关一样，它将远程服务的词汇表转换为主机应用程序的词汇表。但是对于远程服务，它还负责封装远程服务的远程性，比如远程调用如何完成的细节。第二个职责意味着远程网关应该包含一个单独的元素来处理它，我称之为连接。

![](https://martinfowler.com/articles/gateway-pattern/remote-comp.png)

在这种情况下，listAvailableSlots 可能是对配置中读取某个 URL 的远程调用。

```typescript
// equipmentBookingService.ts
export async function listAvailableSlots(equipmentCode: string, duration: number, isEmergency: boolean) : Promise<Slot[]>
{
    const url = new URL(config['equipmentServiceRootUrl'] + '/availableSlots')
    const params = url.searchParams;
    params.set('duration', duration.toString())
    params.set('isEmergency', isEmergency.toString())
    params.set('equipmentCode', equipmentCode)
    const response = await fetch(url)
    const data = await response.json()
    return data
}
```

将上面的步骤拆分两个操作，这样会更加简单与更容易测试。

然后在构建时将此连接添加到网关类。然后公共函数使用在连接中传递的这个。

```typescript
// class BoneFusionGateway
private readonly conn: Connection
  constructor(conn:Connection) {
    this.conn = conn
}

async listSlots(length: Duration) : Promise<Slot[]> {
    const slots = await this.conn("BFSN", length.toMinutes(), false)
    return slots.map(convertSlot)
}
```

网关通常在同一个基础连接上支持多个公共函数。因此，如果我们的治疗应用程序后来需要保留血液过滤机，我们可以向网关添加另一个功能，该功能将使用相同的连接功能，但使用不同的设备代码。网关还可以将来自多个连接的数据合并到一个公共函数中。

当这样的服务调用需要一些配置时，通常明智的做法是将其与使用它的代码分开进行。我们希望治疗计划预约代码能够简单地使用网关，而不需要知道应该如何配置它。一个简单而有用的方法是使用**服务定位器(service locator)**。

```typescript
// class ServiceLocator
boneFusionGateway: BoneFusionGateway

// serviceLocator.ts
export let theServiceLocator: ServiceLocator
```

配置（通常用在程序启动时）

```typescript
theServiceLocator.boneFusionGateway = new BoneFusionGateway(listAvailableSlots)
```

应用程序使用网关

```typescript
const slots = await theServiceLocator.boneFusionGateway.listSlots(Duration.ofHours(2))
```

有了这种设置，我就可以像这样为连接编写一个带有存根的测试

```typescript
it('stubbing the connection', async function() {
  const input: ebs.Slot[] = [
    {duration:  120, equipmentCode: "BFSN", equipmentID: "BF-018",
     date: "2020-05-01", time: "13:00", emergencyOnly: false},
    {duration: 180, equipmentCode: "BFSN", equipmentID: "BF-018",
     date: "2020-05-02", time: "08:00", emergencyOnly: false},
    {duration: 150, equipmentCode: "BFSN", equipmentID: "BF-019",
     date: "2020-04-06", time: "10:00", emergencyOnly: false},
   
  ]
  theServiceLocator.boneFusionGateway = new BoneFusionGateway(async () => input)
  const expected: Slot[] = [
    {duration: Duration.ofHours(2), date: LocalDate.of(2020, 5,1), time: LocalTime.of(13,0),
     model: new EquipmentModel("Marrowvate D12")},
    {duration: Duration.ofHours(3), date: LocalDate.of(2020, 5,2), time: LocalTime.of(8,0),
     model: new EquipmentModel("Marrowvate D12")},
  ]
  expect(await suitableSlots()).toStrictEqual(expected)
});
```

后面都是对例子用网关重构的一些关于测试性的阐述，就不说了。

# 原文链接

https://martinfowler.com/articles/gateway-pattern.html