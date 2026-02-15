# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0-windowsservercore-ltsc2022 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["src/LucidToolServer/LucidToolServer.csproj", "LucidToolServer/"]
RUN dotnet restore "LucidToolServer/LucidToolServer.csproj"

# Copy source code and build
COPY src/ .
WORKDIR /src/LucidToolServer
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-windowsservercore-ltsc2022
WORKDIR /app

# Optional: Trust custom CA certificates
# Customers with internal PKI place their CA root cert in certs/ca-trust/
COPY src/LucidToolServer/certs/ca-trust/ C:/certs/ca-trust/
RUN powershell -Command "Get-ChildItem C:/certs/ca-trust/*.crt -ErrorAction SilentlyContinue | ForEach-Object { certutil -addstore -f 'Root' $_.FullName }"

# Copy published application
COPY --from=build /app/publish .

# Expose both ports
EXPOSE 8080
EXPOSE 8443

# Set environment variables — both HTTP and HTTPS
ENV ASPNETCORE_URLS=http://+:8080;https://+:8443

# Entry point
ENTRYPOINT ["dotnet", "LucidToolServer.dll"]
