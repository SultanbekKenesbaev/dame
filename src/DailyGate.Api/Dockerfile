FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/DailyGate.Shared/DailyGate.Shared.csproj src/DailyGate.Shared/
COPY src/DailyGate.Api/DailyGate.Api.csproj src/DailyGate.Api/
RUN dotnet restore src/DailyGate.Api/DailyGate.Api.csproj
COPY src/DailyGate.Shared/ src/DailyGate.Shared/
COPY src/DailyGate.Api/ src/DailyGate.Api/
RUN dotnet publish src/DailyGate.Api/DailyGate.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl passwd && rm -rf /var/lib/apt/lists/* \
    && groupadd --system dailygate && useradd --system --gid dailygate --home-dir /app --shell /usr/sbin/nologin dailygate \
    && mkdir -p /app/data && chown -R dailygate:dailygate /app
USER dailygate
COPY --from=build --chown=dailygate:dailygate /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DailyGate.Api.dll"]
