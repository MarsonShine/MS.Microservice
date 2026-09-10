FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json nuget.config ./
COPY MS.Microservice.Logging/src/MS.Microservice.Logging.AspNetCore/MS.Microservice.Logging.AspNetCore.csproj MS.Microservice.Logging/src/MS.Microservice.Logging.AspNetCore/
COPY MS.Microservice.Logging/src/MS.Microservice.Logging.Core/MS.Microservice.Logging.Core.csproj MS.Microservice.Logging/src/MS.Microservice.Logging.Core/
COPY samples/Reference/MS.Microservice.Reference.Application/MS.Microservice.Reference.Application.csproj samples/Reference/MS.Microservice.Reference.Application/
COPY samples/Reference/MS.Microservice.Reference.Domain/MS.Microservice.Reference.Domain.csproj samples/Reference/MS.Microservice.Reference.Domain/
COPY samples/Reference/MS.Microservice.Reference.Persistence/MS.Microservice.Reference.Persistence.csproj samples/Reference/MS.Microservice.Reference.Persistence/
COPY samples/Reference/MS.Microservice.Reference.Web/MS.Microservice.Reference.Web.csproj samples/Reference/MS.Microservice.Reference.Web/
COPY src/MS.Microservice.AspNetCore/MS.Microservice.AspNetCore.csproj src/MS.Microservice.AspNetCore/
COPY src/MS.Microservice.Core/MS.Microservice.Core.csproj src/MS.Microservice.Core/
COPY src/MS.Microservice.Domain.Primitives/MS.Microservice.Domain.Primitives.csproj src/MS.Microservice.Domain.Primitives/
COPY src/MS.Microservice.Messaging.Abstractions/MS.Microservice.Messaging.Abstractions.csproj src/MS.Microservice.Messaging.Abstractions/
COPY src/MS.Microservice.Messaging.RabbitMQ/MS.Microservice.Messaging.RabbitMQ.csproj src/MS.Microservice.Messaging.RabbitMQ/
COPY src/MS.Microservice.Messaging.SelfManaged.EFCore/MS.Microservice.Messaging.SelfManaged.EFCore.csproj src/MS.Microservice.Messaging.SelfManaged.EFCore/
COPY src/MS.Microservice.Messaging.Wolverine/MS.Microservice.Messaging.Wolverine.csproj src/MS.Microservice.Messaging.Wolverine/
COPY src/MS.Microservice.Observability/MS.Microservice.Observability.csproj src/MS.Microservice.Observability/
RUN dotnet restore samples/Reference/MS.Microservice.Reference.Web/MS.Microservice.Reference.Web.csproj
COPY . .
RUN dotnet publish samples/Reference/MS.Microservice.Reference.Web/MS.Microservice.Reference.Web.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MS.Microservice.Reference.Web.dll"]
