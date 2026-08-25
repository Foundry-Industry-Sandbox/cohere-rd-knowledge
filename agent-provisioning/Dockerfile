FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["NuGet.config", "."]
COPY ["agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/CohereRndKnowledge.AgentProvisioning.csproj", "agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/"]
RUN dotnet restore "agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/CohereRndKnowledge.AgentProvisioning.csproj" --configfile NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json
COPY agent-provisioning/ agent-provisioning/
WORKDIR /src/agent-provisioning/src/CohereRndKnowledge.AgentProvisioning
RUN dotnet publish "CohereRndKnowledge.AgentProvisioning.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/agents /app/agents
COPY --from=build /src/agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/shared /app/shared
COPY --from=build /src/agent-provisioning/src/CohereRndKnowledge.AgentProvisioning/config/provisioning.json /app/config/provisioning.json
ENTRYPOINT ["dotnet", "CohereRndKnowledge.AgentProvisioning.dll", "--config", "/app/config/provisioning.json", "--agents", "/app/agents"]
