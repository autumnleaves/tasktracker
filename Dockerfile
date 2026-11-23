# syntax=docker/dockerfile:1
# One image: the API, serving the built web app from wwwroot. CI builds it and ships it to
# cargoship inside the deploy bundle (docs/deploy.md), so cargoship never builds anything.

FROM node:24-slim AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY src/TaskTracker.Api/TaskTracker.Api.csproj src/TaskTracker.Api/
RUN dotnet restore src/TaskTracker.Api/TaskTracker.Api.csproj
COPY src/ src/
RUN dotnet publish src/TaskTracker.Api/TaskTracker.Api.csproj -c Release -o /out --no-restore

# Chiseled: no shell or package manager, runs as the non-root `app` user (UID 1654), which
# must own the /data mount (pie.json has pie-deploy make /opt/apps/tasktracker/data that way).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Tasks="Data Source=/data/tasks.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "TaskTracker.Api.dll"]
