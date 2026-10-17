# UntPlan Studio (Beta) – Container-Image. Build-Kontext ist das Wurzelverzeichnis des UntPlan-Repositorys,
# weil das Studio die gemeinsame Fachlogik src/UntPlan.Core (und src/UntPlan.Web.Contracts) verwendet:
#   docker build -f studio/Dockerfile -t untplanstudio .

# Bauen immer auf der Architektur des Build-Rechners; das Ergebnis ist plattformunabhängig (amd64 und arm64)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /quelle
COPY Directory.Build.props ./
COPY src/UntPlan.Web.Contracts/UntPlan.Web.Contracts.csproj src/UntPlan.Web.Contracts/
COPY src/UntPlan.Core/UntPlan.Core.csproj src/UntPlan.Core/
COPY studio/src/UntPlanStudio/UntPlanStudio.csproj studio/src/UntPlanStudio/
RUN dotnet restore studio/src/UntPlanStudio/UntPlanStudio.csproj
COPY src/UntPlan.Web.Contracts/ src/UntPlan.Web.Contracts/
COPY src/UntPlan.Core/ src/UntPlan.Core/
COPY studio/src/UntPlanStudio/ studio/src/UntPlanStudio/
ARG VERSION=1.0.0
RUN dotnet publish studio/src/UntPlanStudio/UntPlanStudio.csproj -c Release -o /app --no-restore -p:UseAppHost=false -p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/aspnet:8.0
LABEL org.opencontainers.image.title="UntPlan Studio (Beta)" \
      org.opencontainers.image.description="Die Desktop-Anwendung UntPlan als Browser-Anwendung (Beta)" \
      org.opencontainers.image.source="https://github.com/florians25/UntPlan"
ENV ASPNETCORE_HTTP_PORTS=8080 \
    STUDIO_DATEN=/data \
    TZ=Europe/Berlin \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app ./
# Datenordner für Schuldateien, Benutzer und Schlüssel; läuft als Benutzer „app“ (ohne Root-Rechte)
RUN mkdir -p /data && chown app:app /data
USER app
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "UntPlanStudio.dll", "--gesundheitspruefung"]
ENTRYPOINT ["dotnet", "UntPlanStudio.dll"]
