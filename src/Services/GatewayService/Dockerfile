FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

# Install curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy Observability library
COPY ["src/Shared/Observability/Observability.csproj", "Shared/Observability/"]
RUN dotnet restore "Shared/Observability/Observability.csproj"
COPY ["src/Shared/Observability/", "Shared/Observability/"]
RUN dotnet build "Shared/Observability/Observability.csproj" -c Release

# Copy and build Gateway Service
COPY ["src/Services/GatewayService/GatewayService.csproj", "Services/GatewayService/"]
RUN dotnet restore "Services/GatewayService/GatewayService.csproj"
COPY ["src/Services/GatewayService/", "Services/GatewayService/"]

WORKDIR "/src/Services/GatewayService"
RUN dotnet build "GatewayService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GatewayService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Add labels for better observability
LABEL service="gateway-service"
LABEL version="1.0.0"
LABEL environment="production"

ENTRYPOINT ["dotnet", "GatewayService.dll"]
