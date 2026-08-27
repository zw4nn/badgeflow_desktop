@echo off
setlocal

dotnet restore BadgeFlow.Desktop.csproj || exit /b 1
dotnet publish BadgeFlow.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish || exit /b 1

set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" (
  echo Inno Setup 6 est requis : https://jrsoftware.org/isdl.php
  exit /b 2
)

"%ISCC%" BadgeFlow-Setup.iss || exit /b 1

echo.
echo Installateur genere : installer-output\BadgeFlow-Setup.exe
endlocal
