FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY LicenseValidation.Api/LicenseValidation.Api.csproj LicenseValidation.Api/
RUN dotnet restore LicenseValidation.Api/LicenseValidation.Api.csproj

COPY LicenseValidation.Api/ LicenseValidation.Api/
RUN dotnet publish LicenseValidation.Api/LicenseValidation.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

RUN apk add --no-cache curl

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish .

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent http://127.0.0.1:8080/health/live > /dev/null || exit 1

ENTRYPOINT ["dotnet", "LicenseValidation.Api.dll"]
