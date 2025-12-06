FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5002

# Install curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy Observability library
COPY ["src/Shared/Observability/Observability.csproj", "Shared/Observability/"]
RUN dotnet restore "Shared/Observability/Observability.csproj"
COPY ["src/Shared/Observability/", "Shared/Observability/"]
RUN dotnet build "Shared/Observability/Observability.csproj" -c Release

# Copy and build Order Service
COPY ["src/Services/OrderService/OrderService.csproj", "Services/OrderService/"]
RUN dotnet restore "Services/OrderService/OrderService.csproj"
COPY ["src/Services/OrderService/", "Services/OrderService/"]

WORKDIR "/src/Services/OrderService"
RUN dotnet build "OrderService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OrderService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Add labels for better observability
LABEL service="order-service"
LABEL version="1.0.0"
LABEL environment="production"

ENTRYPOINT ["dotnet", "OrderService.dll"]
