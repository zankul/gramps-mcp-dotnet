FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore on the csproj files alone so this layer survives source-only changes.
COPY Directory.Build.props ./
COPY src/GrampsMcp.Server/GrampsMcp.Server.csproj src/GrampsMcp.Server/
RUN dotnet restore src/GrampsMcp.Server/GrampsMcp.Server.csproj

COPY src/ src/
RUN dotnet publish src/GrampsMcp.Server/GrampsMcp.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app .

# The aspnet-alpine base image already ships a non-root "app" user; reuse it.
RUN chown -R app:app /app
USER app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# /health only exists under the HTTP transport; skip the probe under stdio (MCP_TRANSPORT=stdio),
# where there's no listener to check and the process itself is the thing to watch.
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD [ "$MCP_TRANSPORT" = "stdio" ] || wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "GrampsMcp.Server.dll"]
