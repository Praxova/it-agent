# ─────────────────────────────────────────────────────────────────
# Praxova Admin Portal — Production Container
# ─────────────────────────────────────────────────────────────────
# Multi-stage build: restore/build in SDK image, run in slim ASP.NET image

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first for layer caching
COPY src/LucidAdmin.Core/LucidAdmin.Core.csproj LucidAdmin.Core/
COPY src/LucidAdmin.Infrastructure/LucidAdmin.Infrastructure.csproj LucidAdmin.Infrastructure/
COPY src/LucidAdmin.Web/LucidAdmin.Web.csproj LucidAdmin.Web/

# Restore dependencies (cached unless csproj files change)
RUN dotnet restore LucidAdmin.Web/LucidAdmin.Web.csproj

# Copy all source code
COPY src/ .

# Build release
WORKDIR /src/LucidAdmin.Web
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install libldap for System.DirectoryServices.Protocols (AD authentication)
RUN apt-get update && \
    apt-get install -y --no-install-recommends libldap-2.5-0 curl && \
    rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app/publish .

# Create data and logs directories
RUN mkdir -p /app/data /app/logs

# Environment defaults
ENV ASPNETCORE_URLS=http://+:5000;https://+:5001
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/lucid-admin.db"
ENV Database__Provider=Sqlite

EXPOSE 5000
EXPOSE 5001

# Health check against the existing endpoint
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:5000/api/health/ || exit 1

ENTRYPOINT ["dotnet", "LucidAdmin.Web.dll"]
