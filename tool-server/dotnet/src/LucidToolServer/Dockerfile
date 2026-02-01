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

# Copy published application
COPY --from=build /app/publish .

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080

# Entry point
ENTRYPOINT ["dotnet", "LucidToolServer.dll"]
