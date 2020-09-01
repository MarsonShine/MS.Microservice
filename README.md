# MS.Microservice
微服务架构设计

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

