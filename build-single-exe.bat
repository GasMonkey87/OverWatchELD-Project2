@echo off
setlocal
dotnet publish OverWatchELD.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-single
echo.
echo Done. Check publish-single\OverWatchELD.exe
pause
