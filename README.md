# MS.Microservice
微服务架构设计

Martin Fowler 对微服务特征的概括：[微服务](https://martinfowler.com/articles/microservices.html#SmartEndpointsAndDumbPipes)

- [上下文边界](docs/Context-Bounded.md)
- [领域命令模式处理程序](docs/Domain-Command-Patterns-Handlers.md)
- [领域命令验证](docs/Domain-Command-Validation.md)
- [贫血领域模型](Anemic-Domain-Model.md)
- [值对象](docs/ValueObject.md)
- [DDD 枚举解决方案](docs/Enumeration.md)
- [CAP 定理](docs/CAP-Theorem.md)
- [CQRS](docs/CQRS.md)
- [事件溯源模式](docs/Event-Source-Pattern.md)
- [最终一致性](docs/Eventual-Consistency.md)
- [架构透明原则](docs/Infrastructure-Ignorance.md) 
- [持久化透明](docs/Persistence-Ignorance.md)
- [报表数据库](docs/Reporting-Database.md)
- [SeedWork](docs/SeedWork.md)
- [接口隔离原则](docs/Separated-Interface.md) 
- [快照模式](docs/Snapshot.md)
- [康威定律](docs/ConwayLaw.md)

# 微服务与 DDD 基本概念

应用程序包括

- 展示模块，这是负责处理 UI 和消费远程服务的
- 领域或业务逻辑模块，这是核心模块，应用程序的领域逻辑
- 数据访问逻辑，它由负责访问数据库(SQL 或 NoSQL)的数据访问组件组成。
- 应用集成逻辑，这包括消息通道，主要是基于消息代理（message brokers）

应用程序必须要高可用，要支持垂直的向外拓展，因为有些子系统还会要求更高的伸缩性。

并且应用程序必须是可以部署到多种架构环境的（多个公共云或本地云），并且理论上还是要跨平台的，要很容易的从 Windows 和 Linux 之间切换。

微服务拆分的好处：

- 让每个服务都相对更小——这样更好管理与解决问题，特别是
  - 对开发者更好理解，并快速开发或解决新的需求与问题
  - 容器开启更快速，能让开发更有效率
  - 也有利于开发 IDE 的快速加载，能使开发更有效率
  - 每个微服务都是可独立部署的，提高了敏捷性，并且也很容易的部署新的版本
-  应用程序的伸缩性，可拓展性更强。如众多拆分的微服务种，某两个服务流量多，我们可以针对这两个服务进行硬件拓展
- 可以独立拆分不同服务给不同的团队，用不同团队擅长的开发语言
- 出现的问题会独立，所以也会更好定位
- 可以使用最新的技术

微服务的缺点：

- 分布式应用，分布式应用为开发带来了很大的复杂性。例如开发者必须要实现内部服务之间的交互（HTTP、AMPQ），这也会给测试带来困难，也增加延时。

- 部署复杂，原来的单体应用只需要部署一套即可，而现在拆分为非常多个小的服务单独部署，并且还要实现高可用，容错性，拓展性。所以还要顺带部署如负载均衡、网络网关等节点，这给管理与维护也带了复杂度

- 原子事务，由于应用的分布式，想要实现原子事务性是不可能的，所以必须要实现多服务之间的最终一致性

- 增加总体资源需求，与单体应用相比需要的资源也要高了很多，因为拆分了不同的服务，所需要的硬件设备，网络带宽等资源都会上升，这些都是为了实现整体服务的高可用、伸缩、容错

- 客户端与服务通信的问题，因为服务很多，客户端要的信息与这些服务接口传递的信息不匹配，所以客户端必须要调用很多服务来组合这些信息，这无疑是效率低下的，要尽可能保持每个服务小，并且由聚合服务返回给客户端。

  还有一个问题就是客户端与服务的网络协议不同，比如客户端与服务端采用的是浏览器友好的 HTTP 协议，而内部服务用的可能是 GRPC 或是 AMPQ 协议，这也带来了一定的复杂性。

- 微服务划分，最后一个难点也是团队争议比较多的点，就是如何划分微服务。请切记尽可能让每个微服务逻辑自洽。

## 微服务分层

- [应用层](docs/basic-concept/Application.md)
  - Web API（.NET,Java,Go）
  - 网络访问（GRPC）
  - API 接口实现
  - 命令与命令事件（Command，Command Handler）
  - 查询器（Query，CQRS 分离模式），如 Dapper,ADO.NET
  - ...
- [领域层](docs/basic-concept/Domain.md)
  - 实体领域模型
  - POCO 实体类
  - 领域实体行为（方法，事件）
  - DDD 模式
    - 领域实体，聚合根
    - 值对象
    - 仓储接口
- [基础设施层](docs/basic-concept/Infrastructure.md)
  - 数据持久化架构
    - 仓储接口的实现
  - 使用 ORM，数据访问层 API（EF，ADO.NET，NOSQL）
  - 其它公共功能基础设施，被其他层引用到的模块都可以放置此
    - Logging，密码安全，搜索引擎等

# 微服务模块组成

- [消息中间件（RabbitMQ）](docs/mq)
- [服务注册发现（Consul）](docs/consul)
- [Grpc](docs/grpc)
- [服务网格](docs/service-mesh)

# 分布式系统模式

- [分布式系统解决方案模式](docs/patterns-of-distributed-systems)

# 分布式锁

- [Redis：Redlock](docs/distribution-lock/redis-distribution-lock.md)
- [关于 Redis 分布式锁实现原理分析以及可能出现的问题](docs/How-To-Do-Distributed-Locking.md)
- Zookeeper

# 关于 Docker 部署

利用 Visual Studio 2019 工具添加 docker 支持，会根据项目结构自动添加两个文件结构

- .dockerfile
- .dockerignore

关于自动生成的 Dockerfile 文件，如果项目没有用到私有 nuget 库的话，一般是没有问题的。如果用了私有库，那么我们还得在 Dockerfile 文件添加 `nuget.config` 文件，来告诉 dokcer 在 restore 时要读取的 nuget 服务地址。举个例子：

```dockerfile
#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
#下面一行添加私有nuget库
COPY ["nuget.config","src/MS.Microservice.Web/"]
#上面一行添加私有nuget库
COPY ["src/MS.Microservice.Web/MS.Microservice.Web.csproj", "src/MS.Microservice.Web/"]
COPY ["src/MS.Microservice.IntegrateEvent/MS.Microservice.IntegrateEvent.csproj", "src/MS.Microservice.IntegrateEvent/"]
COPY ["src/MS.Microservice.Database/MS.Microservice.Database.csproj", "src/MS.Microservice.Database/"]
COPY ["src/MS.Microservice.Repostitory/MS.Microservice.Repostitory.csproj", "src/MS.Microservice.Repostitory/"]
COPY ["src/MS.Microservice.Domain/MS.Microservice.Domain.csproj", "src/MS.Microservice.Domain/"]
COPY ["src/MS.Microservice.Core/MS.Microservice.Core.csproj", "src/MS.Microservice.Core/"]
RUN dotnet restore "src/MS.Microservice.Web/MS.Microservice.Web.csproj"
COPY . .
WORKDIR "/src/src/MS.Microservice.Web"
RUN dotnet build "MS.Microservice.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MS.Microservice.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MS.Microservice.Web.dll"]
```

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="private.server" value="http://192.168.1.101:8001/nuget" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

# 2020-12-24 修改

由于本项目增加了文件 `Directory.Build.props` 以及 `global.json` 文件，所以生成项目是要依赖解决方案的。在上面的的 dockerfile 内容只是复制了各项目的内容，没有 `*.sln` 做上下文环境支撑，所以无法正常运行。

需要将对应的项目目录结构全部拷贝至 docker 环境中，如最新的 dockerfile 内容如下：（⚠️要放在 sln 同目录下）

```dockerfile
#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0-buster-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
WORKDIR /src
COPY ["Directory.Build.props","src/"]
COPY ["global.json","src/"]
COPY ["MS.Microservice.sln","src/"]
COPY ["nuget.config","src/"]
COPY ["src/MS.Microservice.Web/MS.Microservice.Web.csproj", "src/MS.Microservice.Web/"]
COPY ["src/MS.Microservice.IntegrateEvent/MS.Microservice.IntegrateEvent.csproj", "src/MS.Microservice.IntegrateEvent/"]
COPY ["src/MS.Microservice.Database/MS.Microservice.Database.csproj", "src/MS.Microservice.Database/"]
COPY ["src/MS.Microservice.Repostitory/MS.Microservice.Repostitory.csproj", "src/MS.Microservice.Repostitory/"]
COPY ["src/MS.Microservice.Domain/MS.Microservice.Domain.csproj", "src/MS.Microservice.Domain/"]
COPY ["src/MS.Microservice.Core/MS.Microservice.Core.csproj", "src/MS.Microservice.Core/"]
RUN dotnet restore "src/MS.Microservice.Web/MS.Microservice.Web.csproj"
COPY . .

WORKDIR "/src/src/MS.Microservice.Web"
RUN dotnet build "MS.Microservice.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MS.Microservice.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MS.Microservice.Web.dll"]
```

