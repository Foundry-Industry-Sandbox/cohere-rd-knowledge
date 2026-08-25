# Build context: repository root (same as .github/workflows/publish-container-images.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NuGet.config .
COPY frontend/src/WebApp/Cohere.AgenticRDKnowledge.WebApp.csproj frontend/src/WebApp/
RUN dotnet restore frontend/src/WebApp/Cohere.AgenticRDKnowledge.WebApp.csproj --configfile NuGet.config --source https://packagefeedproxy.microsoft.io/nuget/v3/index.json

COPY frontend/src/WebApp/ frontend/src/WebApp/

RUN dotnet publish frontend/src/WebApp/Cohere.AgenticRDKnowledge.WebApp.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ApiBaseUrl=

EXPOSE 8080
ENTRYPOINT ["dotnet", "Cohere.AgenticRDKnowledge.WebApp.dll"]
