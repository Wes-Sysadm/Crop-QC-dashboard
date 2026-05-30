FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["sh", "-c", "dotnet CropQc.Web.dll --urls http://0.0.0.0:${PORT:-8080}"]
