param([switch]$Demo)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$binaryPath = Join-Path $projectRoot 'bin/Release/net7.0-windows/CodexIsland.exe'
if (-not (Test-Path -LiteralPath $binaryPath)) {
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.build'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    dotnet build (Join-Path $projectRoot 'CodexIsland.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw '编译失败，请检查 .NET SDK。' }
}
$launchOptions = @{ FilePath = $binaryPath; WorkingDirectory = $projectRoot; WindowStyle = 'Hidden' }
if ($Demo) { $launchOptions.ArgumentList = '--demo' }
Start-Process @launchOptions
