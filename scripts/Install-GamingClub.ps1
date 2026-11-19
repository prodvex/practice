[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)][string]$ArchivePath,
    [string]$InstallPath = "$env:USERPROFILE\GamingClub",
    [string]$SqlServer = "localhost\SQLEXPRESS",
    [string]$DatabaseName = "GamingClub",
    [string]$JwtSecret,
    [switch]$SkipDatabase,
    [switch]$RunTests,
    [switch]$StartApi
)
$ErrorActionPreference = 'Stop'
$LogPath = Join-Path $InstallPath 'install.log'
function Write-Log([string]$Message) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message"
    Write-Host $line
    if (Test-Path (Split-Path $LogPath -Parent)) { Add-Content -Path $LogPath -Value $line }
}
function Require-Command([string]$Name,[string]$Hint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { Write-Log "Не найдено: $Name. $Hint"; return $false }
    return $true
}
try {
    if (-not $ArchivePath) {
        $ArchivePath = Get-ChildItem -Path (Get-Location) -File -Include *.zip,*.7z -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $ArchivePath) { throw 'Укажите путь к ZIP-архиву параметром -ArchivePath.' }
    if (-not (Test-Path $ArchivePath)) { throw "Архив не найден: $ArchivePath" }
    if ([IO.Path]::GetExtension($ArchivePath).ToLowerInvariant() -ne '.zip') { throw 'Поддерживается ZIP-архив. Сначала распакуйте 7z/rar в ZIP.' }
    New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
    New-Item -ItemType File -Force -Path $LogPath | Out-Null
    Write-Log "Архив: $ArchivePath"
    Write-Log "Каталог установки: $InstallPath"
    $extract = Join-Path $InstallPath '_archive'
    if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
    New-Item -ItemType Directory -Force -Path $extract | Out-Null
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $extract -Force
    $root = Get-ChildItem -Path $extract -Directory | Select-Object -First 1
    if (-not $root) { $root = Get-Item $extract }
    Write-Log "Архив распакован. Найден корень: $($root.FullName)"
    Get-ChildItem -Path $root.FullName -Force | ForEach-Object { Copy-Item $_.FullName -Destination $InstallPath -Recurse -Force }
    Write-Log 'Файлы проекта скопированы.'
    $envFile = Join-Path $InstallPath '.env.local.ps1'
    if (-not $JwtSecret) { $JwtSecret = [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 })) }
    $connection = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;TrustServerCertificate=True"
    @("`$env:ConnectionStrings__GamingClub = '$connection'", "`$env:GAMINGCLUB_JWT_SECRET = '$JwtSecret'") | Set-Content -Path $envFile -Encoding UTF8
    Write-Log "Локальная конфигурация создана: $envFile"
    $dotnet = Require-Command 'dotnet' 'Установите .NET SDK 8.'
    if ($dotnet) {
        dotnet --version | Out-File -Append $LogPath
        if (Test-Path (Join-Path $InstallPath 'src\GamingClub.API\GamingClub.API.csproj')) {
            dotnet restore (Join-Path $InstallPath 'src\GamingClub.API\GamingClub.API.csproj') | Tee-Object -FilePath $LogPath -Append
        }
    }
    if (-not $SkipDatabase) {
        $sqlcmd = Require-Command 'sqlcmd' 'Установите SQL Server Command Line Utilities или запустите скрипты вручную в SSMS.'
        $schema = Join-Path $InstallPath 'database\schema.sql'; $seed = Join-Path $InstallPath 'database\seed.sql'
        if ($sqlcmd -and (Test-Path $schema) -and (Test-Path $seed)) {
            Write-Log 'Создание базы данных из schema.sql.'
            sqlcmd -S $SqlServer -E -i $schema -b | Tee-Object -FilePath $LogPath -Append
            Write-Log 'Загрузка синтетических данных из seed.sql.'
            sqlcmd -S $SqlServer -d $DatabaseName -E -i $seed -b | Tee-Object -FilePath $LogPath -Append
        } else { Write-Log 'Шаг базы данных пропущен. Проверьте sqlcmd и файлы SQL.' }
    }
    if ($RunTests -and $dotnet) {
        $tests = Join-Path $InstallPath 'tests\GamingClub.Tests\GamingClub.Tests.csproj'
        if (Test-Path $tests) { dotnet test $tests --no-restore | Tee-Object -FilePath $LogPath -Append }
    }
    if ($StartApi -and $dotnet) {
        . $envFile
        $api = Join-Path $InstallPath 'src\GamingClub.API'
        Write-Log 'Запуск API на http://localhost:5080.'
        Start-Process -FilePath 'dotnet' -ArgumentList "run --project `"$api`" --urls http://localhost:5080" -WorkingDirectory $InstallPath
    }
    Write-Log 'Установка завершена.'
    Write-Host "`nПроект установлен в $InstallPath"
    Write-Host "Для локальной конфигурации выполните: . `"$envFile`""
    Write-Host 'Swagger: http://localhost:5080/swagger'
} catch { Write-Error $_; exit 1 }
