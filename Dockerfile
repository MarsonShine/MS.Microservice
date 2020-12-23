#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
WORKDIR /app
EXPOSE 5000
# EXPOSE 443

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
# WORKDIR /src
COPY ["Directory.Build.props","MS.Microservice.Web/"]
COPY ["nuget.config","MS.Microservice.Web/"]
COPY ["/src/MS.Microservice.Web/MS.Microservice.Web.csproj", "MS.Microservice.Web/"]
COPY ["/src/MS.Microservice.IntegrateEvent/MS.Microservice.IntegrateEvent.csproj", "MS.Microservice.IntegrateEvent/"]
COPY ["/src/MS.Microservice.Database/MS.Microservice.Database.csproj", "MS.Microservice.Database/"]
COPY ["/src/MS.Microservice.Repostitory/MS.Microservice.Repostitory.csproj", "MS.Microservice.Repostitory/"]
COPY ["/src/MS.Microservice.Domain/MS.Microservice.Domain.csproj", "MS.Microservice.Domain/"]
COPY ["/src/MS.Microservice.Core/MS.Microservice.Core.csproj", "MS.Microservice.Core/"]
RUN dotnet restore "MS.Microservice.Web/MS.Microservice.Web.csproj"
COPY . .

WORKDIR "/MS.Microservice.Web"
RUN dotnet build "MS.Microservice.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MS.Microservice.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MS.Microservice.Web.dll"]