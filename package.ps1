param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$publishPath = Join-Path $projectRoot 'staging/publish'
$buildPath = Join-Path $projectRoot 'staging/build'
if (-not $SkipBuild) {
    dotnet publish (Join-Path $projectRoot 'CodexIsland.csproj') -c Release --self-contained false -o $publishPath "-p:OutputPath=$buildPath/" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
$distPath = Join-Path $projectRoot 'dist'
$packagePath = Join-Path $distPath 'OrbitQuota-win-x64'
New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
foreach ($fileName in @('CodexIsland.dll','CodexIsland.deps.json','CodexIsland.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path $publishPath $fileName) -Destination (Join-Path $packagePath $fileName) -Force
}
Copy-Item -LiteralPath (Join-Path $publishPath 'CodexIsland.exe') -Destination (Join-Path $packagePath 'OrbitQuota.exe') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $packagePath 'LICENSE') -Force
@'
OrbitQuota - Windows x64

1. Install Microsoft .NET 10 Desktop Runtime (x64).
2. Sign in to the official Codex CLI or Desktop app.
3. Extract the whole ZIP, then open OrbitQuota.exe.
   Keep the DLL and JSON runtime files beside the EXE.

Hover to peek. Click for Quota / Usage. Drag to move.
Right-click for settings and Exit. Tray left-click reopens the window.
USD values are API-equivalent estimates, not subscription charges.

Source and help: https://github.com/shenaodezhanghu/orbitquota
'@ | Set-Content -LiteralPath (Join-Path $packagePath 'START-HERE.txt') -Encoding UTF8
$archivePath = Join-Path $distPath 'OrbitQuota-win-x64.zip'
$packageFiles = @('OrbitQuota.exe','CodexIsland.dll','CodexIsland.deps.json','CodexIsland.runtimeconfig.json','LICENSE','START-HERE.txt') | ForEach-Object { Join-Path $packagePath $_ }
Compress-Archive -LiteralPath $packageFiles -DestinationPath $archivePath -Force
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  OrbitQuota-win-x64.zip" | Set-Content -LiteralPath (Join-Path $distPath 'SHA256SUMS.txt') -Encoding ASCII
Write-Output $archivePath
