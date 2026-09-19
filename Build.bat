@echo off
echo Building portable DiskMonitor...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
echo.
echo Build complete.
echo Opening EXE folder...
explorer "bin\Release\net8.0-windows\win-x64\publish"
pause
