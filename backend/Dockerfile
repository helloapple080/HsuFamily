# 後端 Dockerfile — .NET 10 Multi-stage Build
FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src

COPY ["FamilyPortal.Api/FamilyPortal.Api.csproj", "FamilyPortal.Api/"]
COPY ["FamilyPortal.Application/FamilyPortal.Application.csproj", "FamilyPortal.Application/"]
COPY ["FamilyPortal.Domain/FamilyPortal.Domain.csproj", "FamilyPortal.Domain/"]
COPY ["FamilyPortal.Infrastructure/FamilyPortal.Infrastructure.csproj", "FamilyPortal.Infrastructure/"]

RUN dotnet restore "FamilyPortal.Api/FamilyPortal.Api.csproj"

COPY . .
RUN dotnet publish "FamilyPortal.Api/FamilyPortal.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10 AS runtime
WORKDIR /app
EXPOSE 5001

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN mkdir -p /app/keys && chown -R app:app /app
USER app

ENTRYPOINT ["dotnet", "FamilyPortal.Api.dll"]
