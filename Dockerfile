FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json nuget.config ./
COPY MS.Microservice.slnx ./

COPY src/MS.Microservice.Core/MS.Microservice.Core.csproj src/MS.Microservice.Core/
COPY src/MS.Microservice.Domain/MS.Microservice.Domain.csproj src/MS.Microservice.Domain/
COPY src/MS.Microservice.Infrastructure/MS.Microservice.Infrastructure.csproj src/MS.Microservice.Infrastructure/
COPY src/MS.Microservice.Web/MS.Microservice.Web.csproj src/MS.Microservice.Web/
COPY MS.Microservice.Swagger/MS.Microservice.Swagger.csproj MS.Microservice.Swagger/
COPY MS.Microservice.Logging/src/MS.Microservice.Logging.Core/MS.Microservice.Logging.Core.csproj MS.Microservice.Logging/src/MS.Microservice.Logging.Core/
COPY MS.Microservice.Logging/src/MS.Microservice.Logging.AspNetCore/MS.Microservice.Logging.AspNetCore.csproj MS.Microservice.Logging/src/MS.Microservice.Logging.AspNetCore/
COPY MS.Microservice.Logging/src/MS.Microservice.Logging.NLog/MS.Microservice.Logging.NLog.csproj MS.Microservice.Logging/src/MS.Microservice.Logging.NLog/

RUN dotnet restore src/MS.Microservice.Web/MS.Microservice.Web.csproj

COPY . .

RUN dotnet publish src/MS.Microservice.Web/MS.Microservice.Web.csproj \
    -c Release \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MS.Microservice.Web.dll"]
