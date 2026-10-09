# syntax=docker/dockerfile:1.7
# Build context is the repository root:  docker build -f src/PdfBuilder.Api/Dockerfile .

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Project files first, so the restore layer stays cached until dependencies actually change.
COPY Directory.Build.props Directory.Packages.props PdfBuilder.slnx ./
COPY src/PdfBuilder.Contracts/PdfBuilder.Contracts.csproj           src/PdfBuilder.Contracts/
COPY src/PdfBuilder.Domain/PdfBuilder.Domain.csproj                 src/PdfBuilder.Domain/
COPY src/PdfBuilder.Application/PdfBuilder.Application.csproj       src/PdfBuilder.Application/
COPY src/PdfBuilder.Infrastructure/PdfBuilder.Infrastructure.csproj src/PdfBuilder.Infrastructure/
COPY src/PdfBuilder.Api/PdfBuilder.Api.csproj                       src/PdfBuilder.Api/
RUN dotnet restore src/PdfBuilder.Api/PdfBuilder.Api.csproj

COPY src/ src/
COPY templates/ templates/
RUN dotnet publish src/PdfBuilder.Api/PdfBuilder.Api.csproj \
      -c Release -o /app/publish --no-restore -p:UseAppHost=false

# ---------- runtime ----------
# Ubuntu 24.04 (noble) is one of Playwright's officially supported distributions.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final

ENV DEBIAN_FRONTEND=noninteractive \
    ASPNETCORE_HTTP_PORTS=8080 \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    Storage__RootPath=/data/files \
    TemplateStore__RootPath=/app/Templates

# Chromium renders missing-glyph boxes without fonts installed, which is most visible on
# non-Latin scripts and emoji. Add more families here if your templates need other scripts.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates fontconfig \
      fonts-liberation fonts-dejavu-core fonts-noto-core fonts-noto-cjk fonts-noto-color-emoji \
 && fc-cache -f \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Install Chromium with the driver shipped inside the Microsoft.Playwright package, which
# guarantees the browser build matches the library version. No PowerShell required.
RUN set -eux; \
    case "$(dpkg --print-architecture)" in \
      amd64) pw=linux-x64 ;; \
      arm64) pw=linux-arm64 ;; \
      *) echo "unsupported architecture" >&2; exit 1 ;; \
    esac; \
    ./.playwright/node/$pw/node ./.playwright/package/cli.js install --with-deps chromium; \
    rm -rf /var/lib/apt/lists/*; \
    mkdir -p /data/files; \
    chown -R app:app /data /ms-playwright

USER app
EXPOSE 8080
VOLUME ["/data"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD ["/bin/sh", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/ready HTTP/1.0\r\n\r\n' >&3 && head -n1 <&3 | grep -q 200"]

ENTRYPOINT ["dotnet", "PdfBuilder.Api.dll"]
