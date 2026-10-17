# UntPlanMobile – Container-Image. Build-Kontext ist das Wurzelverzeichnis des UntPlan-Repositorys (wie bei UntPlanWeb):
#   docker build -f mobile/Dockerfile -t untplanmobile .
# Der Container lauscht auf Port 8081 und holt alle Daten von UntPlanWeb (UNTPLANWEB_URL).

# Bauen immer auf der Architektur des Build-Rechners; das Ergebnis ist plattformunabhängig (amd64 und arm64)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /quelle
COPY Directory.Build.props ./
COPY mobile/src/UntPlanMobile/UntPlanMobile.csproj mobile/src/UntPlanMobile/
RUN dotnet restore mobile/src/UntPlanMobile/UntPlanMobile.csproj
COPY mobile/src/UntPlanMobile/ mobile/src/UntPlanMobile/
# gemeinsame Update-Prüfung mit UntPlanWeb (in UntPlanMobile.csproj eingebunden)
COPY web/src/UntPlanWeb/Dienste/Updatepruefung.cs web/src/UntPlanWeb/Dienste/
ARG VERSION=1.0.0
RUN dotnet publish mobile/src/UntPlanMobile/UntPlanMobile.csproj -c Release -o /app --no-restore -p:UseAppHost=false -p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/aspnet:8.0
LABEL org.opencontainers.image.title="UntPlanMobile" \
      org.opencontainers.image.description="Stundenplan-App fürs Telefon (PWA) – Mobilbegleiter zu UntPlanWeb" \
      org.opencontainers.image.source="https://github.com/florians25/UntPlan"
ENV ASPNETCORE_HTTP_PORTS=8081 \
    UNTPLANWEB_URL=http://untplanweb:8080 \
    UNTPLANMOBILE_DATEN=/data \
    TZ=Europe/Berlin \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app ./
# Datenordner: Profile je Gerät, Push-Abos, VAPID- und Data-Protection-Schlüssel
RUN mkdir -p /data && chown app:app /data
VOLUME /data
USER app
EXPOSE 8081
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 CMD ["dotnet", "UntPlanMobile.dll", "--gesundheitspruefung"]
ENTRYPOINT ["dotnet", "UntPlanMobile.dll"]
