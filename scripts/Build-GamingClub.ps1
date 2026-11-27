[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$RunTests,
    [switch]$BuildAndroid,
    [switch]$PublishDemo,
    [string]$OutputPath = 'artifacts'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
function Require-Command([string]$Name,[string]$Hint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "Не найдено $Name. $Hint" }
}
function Invoke-Step([string]$Title, [scriptblock]$Action) {
    Write-Host "`n=== $Title ===" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Шаг завершился с кодом ${LASTEXITCODE}: $Title" }
}
Require-Command 'dotnet' 'Установите .NET SDK 8.'
if (Get-Command python -ErrorAction SilentlyContinue) {
    Invoke-Step 'Статическая проверка' { python scripts/static_audit.py }
}
Invoke-Step 'Восстановление API' { dotnet restore src/GamingClub.API/GamingClub.API.csproj }
Invoke-Step 'Сборка API' { dotnet build src/GamingClub.API/GamingClub.API.csproj -c $Configuration --no-restore }
Invoke-Step 'Сборка WPF' { dotnet build src/GamingClub.Desktop/GamingClub.Desktop.csproj -c $Configuration -p:EnableWindowsTargeting=true }
if ($PublishDemo) {
    Invoke-Step 'Автономная сборка API для Windows x64' {
        dotnet publish src/GamingClub.API/GamingClub.API.csproj -c $Configuration -r win-x64 --self-contained true -o builds/windows/api
    }
    Invoke-Step 'Автономная сборка WPF для Windows x64' {
        dotnet publish src/GamingClub.Desktop/GamingClub.Desktop.csproj -c $Configuration -r win-x64 --self-contained true -o builds/windows/desktop
    }
}
if ($RunTests) {
    Invoke-Step 'Тесты .NET и покрытие' {
        dotnet test tests/GamingClub.Tests/GamingClub.Tests.csproj -c $Configuration --collect:'XPlat Code Coverage' --logger 'console;verbosity=normal'
    }
}
if ($BuildAndroid) {
    $android = Join-Path $root 'mobile/GamingClub.Android'
    $wrapper = Join-Path $android 'gradlew.bat'
    if (Test-Path $wrapper) {
        Invoke-Step 'Сборка Android Debug APK' { & $wrapper -p $android ':app:assembleDebug' }
    } elseif (Get-Command gradle -ErrorAction SilentlyContinue) {
        Invoke-Step 'Сборка Android Debug APK' { gradle -p $android ':app:assembleDebug' }
    } else {
        throw 'Для Android нужен gradlew.bat или установленный Gradle. Android Studio может использовать этот проект без Wrapper.'
    }
    $apk = Join-Path $android 'app/build/outputs/apk/debug/app-debug.apk'
    Copy-Item $apk $OutputPath -Force
    if ($PublishDemo) {
        New-Item -ItemType Directory -Force -Path builds/android | Out-Null
        Copy-Item $apk builds/android/GamingClub.apk -Force
    }
}
Write-Host "`nСборка завершена. Артефакты и результаты тестов находятся в $OutputPath" -ForegroundColor Green
