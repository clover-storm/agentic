# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ConcurrencyPattern.sln .
COPY src/ConcurrencyPattern.Api/ConcurrencyPattern.Api.csproj src/ConcurrencyPattern.Api/
COPY src/ConcurrencyPattern.Core/ConcurrencyPattern.Core.csproj src/ConcurrencyPattern.Core/
COPY src/ConcurrencyPattern.Infrastructure/ConcurrencyPattern.Infrastructure.csproj src/ConcurrencyPattern.Infrastructure/
COPY src/ConcurrencyPattern.Infrastructure.Redis/ConcurrencyPattern.Infrastructure.Redis.csproj src/ConcurrencyPattern.Infrastructure.Redis/
COPY src/ConcurrencyPattern.Mediator/ConcurrencyPattern.Mediator.csproj src/ConcurrencyPattern.Mediator/
COPY src/ConcurrencyPattern.SequentialProcessor/ConcurrencyPattern.SequentialProcessor.csproj src/ConcurrencyPattern.SequentialProcessor/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY src/ src/

# Build and publish
RUN dotnet publish src/ConcurrencyPattern.Api/ConcurrencyPattern.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos "" appuser

COPY --from=build /app/publish .

# Set ownership
RUN chown -R appuser:appuser /app

USER appuser

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "ConcurrencyPattern.Api.dll"]
