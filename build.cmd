@echo off
rem Builds a single self-contained KennelBridge.exe into dist\ (needs the .NET 8+ SDK from https://dot.net)
dotnet publish "%~dp0KennelBridge.csproj" -c Release -o "%~dp0dist"
pause
