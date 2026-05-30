FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY CropQc.sln ./
COPY src/CropQc.Shared/CropQc.Shared.csproj src/CropQc.Shared/
COPY src/CropQc.Data/CropQc.Data.csproj src/CropQc.Data/
COPY src/CropQc.QcStation/CropQc.QcStation.csproj src/CropQc.QcStation/
COPY src/CropQc.QcStation.WinForms/CropQc.QcStation.WinForms.csproj src/CropQc.QcStation.WinForms/
COPY src/CropQc.Web/CropQc.Web.csproj src/CropQc.Web/
RUN dotnet restore src/CropQc.Web/CropQc.Web.csproj
RUN dotnet restore src/CropQc.QcStation.WinForms/CropQc.QcStation.WinForms.csproj -r win-x86 -p:EnableWindowsTargeting=true -p:Platform=x86 -p:PlatformTarget=x86 -p:RuntimeIdentifier=win-x86 -p:SelfContained=false

COPY src/CropQc.Shared/ src/CropQc.Shared/
COPY src/CropQc.Data/ src/CropQc.Data/
COPY src/CropQc.QcStation/ src/CropQc.QcStation/
COPY src/CropQc.QcStation.WinForms/ src/CropQc.QcStation.WinForms/
COPY src/CropQc.Web/ src/CropQc.Web/
RUN echo "Publishing QC Station WinForms payload" \
    && echo "Target output path: /src/src/CropQc.Web/App_Data/QcStationWinForms" \
    && dotnet publish src/CropQc.QcStation.WinForms/CropQc.QcStation.WinForms.csproj -c Release -r win-x86 --self-contained false -p:EnableWindowsTargeting=true -p:Platform=x86 -p:PlatformTarget=x86 -p:RuntimeIdentifier=win-x86 -p:SelfContained=false -p:PublishSingleFile=false -o src/CropQc.Web/App_Data/QcStationWinForms --no-restore \
    && test -f src/CropQc.Web/App_Data/QcStationWinForms/CropQc.QcStation.WinForms.exe \
    && echo "QC Station WinForms payload published: src/CropQc.Web/App_Data/QcStationWinForms/CropQc.QcStation.WinForms.exe"
RUN dotnet publish src/CropQc.Web/CropQc.Web.csproj -c Release -o /app/publish --no-restore \
    && test -f /app/publish/CropQc.Web.dll \
    && test -f /app/publish/CropQc.Shared.dll \
    && test -f /app/publish/App_Data/QcStationWinForms/CropQc.QcStation.WinForms.exe

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["sh", "-c", "dotnet CropQc.Web.dll --urls http://0.0.0.0:${PORT:-8080}"]
