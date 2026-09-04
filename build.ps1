$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src\CodexGauge\CodexGauge.csproj'
$outputPath = Join-Path $PSScriptRoot 'dist\CodexGauge'
dotnet publish $projectPath -c Release -o $outputPath --self-contained false
if ($LASTEXITCODE -ne 0) { throw 'Сборка завершилась с ошибкой' }
Write-Host "Готово: $outputPath\CodexGauge.exe"
