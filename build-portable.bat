@echo off
setlocal
cd /d "%~dp0"
dotnet restore || pause
dotnet publish BadgeFlow.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish || pause
echo.
echo BadgeFlow Desktop est dans le dossier publish.
pause
