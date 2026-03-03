# Build stage — needs clang and zlib for Native AOT
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# Copy project files and build props first for layer caching
COPY CarbonFiles.slnx .
COPY src/Directory.Build.props src/
COPY src/CarbonFiles.Core/CarbonFiles.Core.csproj src/CarbonFiles.Core/
COPY src/CarbonFiles.Infrastructure/CarbonFiles.Infrastructure.csproj src/CarbonFiles.Infrastructure/
COPY src/CarbonFiles.Api/CarbonFiles.Api.csproj src/CarbonFiles.Api/
COPY src/CarbonFiles.Migrator/CarbonFiles.Migrator.csproj src/CarbonFiles.Migrator/
RUN dotnet restore src/CarbonFiles.Api/CarbonFiles.Api.csproj && \
    dotnet restore src/CarbonFiles.Migrator/CarbonFiles.Migrator.csproj

# Copy everything and publish both
COPY . .
RUN dotnet publish src/CarbonFiles.Api -c Release -r linux-x64 -o /app/api && \
    dotnet publish src/CarbonFiles.Migrator -c Release -o /app/migrator

# Runtime — AOT binary needs no .NET runtime, but Migrator still does
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app

COPY --from=build /app/api ./
COPY --from=build /app/migrator ./migrator/

RUN mkdir -p /app/data && chmod 777 /app/data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Run migrator then start API
ENTRYPOINT ["sh", "-c", "dotnet ./migrator/CarbonFiles.Migrator.dll && exec ./CarbonFiles.Api"]
