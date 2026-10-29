# Runs the benchmark set several times on the current commit, each pass in a fresh process, and writes the
# samples and a summary to benchmarks/results/<version>/: pass-1.json ... and summary.md. Give the machine
# nothing else to do while it runs (about 4 minutes a pass).
#
#   powershell -ExecutionPolicy Bypass -File benchmarks/run.ps1 [-Passes 3] [-Out <folder>]
param([int]$Passes = 3, [string]$Out = '')

$ErrorActionPreference = 'Continue'   # native tools write progress to stderr; their exit codes decide
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$version = ([regex]::Match((Get-Content -Raw src/PyLite/PyLite.csproj), '<Version>(.*?)</Version>')).Groups[1].Value
if ($Out -eq '') { $Out = Join-Path 'benchmarks/results' $version }
if (-not [IO.Path]::IsPathRooted($Out)) { $Out = Join-Path $root $Out }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
Get-ChildItem -Path $Out -Filter 'pass-*.json' | Remove-Item

& dotnet build tools/PyLite.PerfBench -c Release -nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'build failed'; exit 1 }
$exe = Join-Path $root 'tools/PyLite.PerfBench/bin/Release/net472/Outbridge.PyLite.PerfBench.exe'

for ($i = 1; $i -le $Passes; $i++)
{
    Write-Host ('pass {0} of {1}' -f $i, $Passes)
    & $exe --full --json (Join-Path $Out ('pass-{0}.json' -f $i))
    if ($LASTEXITCODE -ne 0) { Write-Host ('pass {0} failed' -f $i); exit 1 }
}
& $exe --summarize $Out
exit $LASTEXITCODE
