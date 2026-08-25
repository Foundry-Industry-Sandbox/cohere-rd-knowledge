FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["NuGet.config", "."]
COPY ["backend/src/RndKnowledgeMining.Governance/RndKnowledgeMining.Governance.csproj", "backend/src/RndKnowledgeMining.Governance/"]
COPY ["backend/src/RndKnowledgeMining.Mcp/RndKnowledgeMining.Mcp.csproj", "backend/src/RndKnowledgeMining.Mcp/"]
COPY ["agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/agents/", "agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/agents/"]
RUN dotnet restore "backend/src/RndKnowledgeMining.Mcp/RndKnowledgeMining.Mcp.csproj" --configfile NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json
COPY backend/src/RndKnowledgeMining.Governance/ backend/src/RndKnowledgeMining.Governance/
COPY backend/src/RndKnowledgeMining.Mcp/ backend/src/RndKnowledgeMining.Mcp/
COPY dataset-seed/ dataset-seed/
COPY policies/hls_policies.txt policies/hls_policies.txt
WORKDIR /src/backend/src/RndKnowledgeMining.Mcp
RUN dotnet publish "RndKnowledgeMining.Mcp.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/policies/hls_policies.txt /app/policies/hls_policies.txt
COPY --from=build /src/dataset-seed /app/dataset-seed
ENV Dataset__RootPath=/app/dataset-seed
ENV PolicySeed__PolicyFilePath=/app/policies/hls_policies.txt
ENTRYPOINT ["dotnet", "RndKnowledgeMining.Mcp.dll"]
