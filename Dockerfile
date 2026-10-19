FROM mcr.microsoft.com/dotnet/sdk:9.0@sha256:cb9d975bf57fd1b0915858d1db1184bea20f7f746f0536323fcab49673144e8c AS build
WORKDIR /src

COPY CropQc.sln ./
COPY src/CropQc.Shared/CropQc.Shared.csproj src/CropQc.Shared/
COPY src/CropQc.Data/CropQc.Data.csproj src/CropQc.Data/
COPY src/CropQc.Web/CropQc.Web.csproj src/CropQc.Web/
RUN dotnet restore src/CropQc.Web/CropQc.Web.csproj

COPY src/CropQc.Shared/ src/CropQc.Shared/
COPY src/CropQc.Data/ src/CropQc.Data/
COPY src/CropQc.Web/ src/CropQc.Web/
RUN dotnet publish src/CropQc.Web/CropQc.Web.csproj -c Release -o /app/publish --no-restore \
    && test -f /app/publish/CropQc.Web.dll \
    && test -f /app/publish/CropQc.Shared.dll \
    && test -f /app/publish/CropQc.Data.dll

# pg_dump refuses to back up a newer server with an older client. Production is
# PostgreSQL 18, so install the matching client from PostgreSQL's signed PGDG repo.
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble@sha256:7da398155e0d6439cf0058bc1afa937817a03e320714b876a5f58a7d64f79d50 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && install -d /usr/share/postgresql-common/pgdg \
    && curl --fail --silent --show-error https://www.postgresql.org/media/keys/ACCC4CF8.asc \
        --output /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt noble-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-18 poppler-utils tesseract-ocr \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
COPY scripts/start-truck-receipt-web.sh /app/start-truck-receipt-web.sh

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["sh", "-c", "if [ \"$#\" -gt 0 ]; then exec \"$@\"; else exec sh /app/start-truck-receipt-web.sh; fi", "--"]
