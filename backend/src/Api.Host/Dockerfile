FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["NuGet.config", "."]
COPY ["backend/src/Api.Host/CohereRndKnowledgeMining.Api.Host.csproj", "backend/src/Api.Host/"]
COPY ["backend/src/RndKnowledgeMining.Governance/RndKnowledgeMining.Governance.csproj", "backend/src/RndKnowledgeMining.Governance/"]
COPY ["backend/src/RndKnowledgeMining.Mcp/RndKnowledgeMining.Mcp.csproj", "backend/src/RndKnowledgeMining.Mcp/"]
RUN dotnet restore "backend/src/Api.Host/CohereRndKnowledgeMining.Api.Host.csproj" --configfile NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json
COPY backend/src/Api.Host/ backend/src/Api.Host/
COPY backend/src/RndKnowledgeMining.Governance/ backend/src/RndKnowledgeMining.Governance/
COPY backend/src/RndKnowledgeMining.Mcp/ backend/src/RndKnowledgeMining.Mcp/
COPY dataset-seed/ dataset-seed/
WORKDIR /src/backend/src/Api.Host
RUN dotnet publish "CohereRndKnowledgeMining.Api.Host.csproj" -c Release -o /app/publish /p:UseAppHost=false -m:1 --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/dataset-seed /app/dataset-seed
ENV Dataset__RootPath=/app/dataset-seed
ENTRYPOINT ["dotnet", "CohereRndKnowledgeMining.Api.Host.dll"]
