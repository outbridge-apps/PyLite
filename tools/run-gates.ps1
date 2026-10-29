# Runs the gates of a release on the current commit and writes the report: the xUnit suite (the everyday
# filter, then the machine-sensitive tests), the conformance cases, the differential corpus against
# IronPython 3.4.2 and the adversarial corpus. Exit 1 when any gate fails. Give the machine nothing else to do
# while it runs (about 12 minutes): some tests and every probe measure time.
#
#   powershell -ExecutionPolicy Bypass -File tools/run-gates.ps1 [-Out docs/test-report.md]
param([string]$Out = 'docs/test-report.md')

$ErrorActionPreference = 'Continue'   # native tools write progress to stderr; their exit codes decide
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$work = Join-Path ([IO.Path]::GetTempPath()) ('pylite-gates-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$gates = New-Object System.Collections.Generic.List[object]
$failed = $false

function Add-Gate([string]$name, [bool]$ok, [string]$result, [TimeSpan]$took)
{
    $script:gates.Add([pscustomobject]@{ Name = $name; Ok = $ok; Result = $result; Took = $took })
    if (-not $ok) { $script:failed = $true }
    $mark = 'FAIL'
    if ($ok) { $mark = 'ok' }
    Write-Host ('{0,-34} {1,-5} {2}  ({3:N0} s)' -f $name, $mark, $result, $took.TotalSeconds)
}

function Read-Trx([string]$path)
{
    if (-not (Test-Path $path)) { return $null }
    $x = [xml](Get-Content -Raw -Path $path)
    return $x.TestRun.ResultSummary.Counters
}

# ---- what the numbers depend on (no host name) ----
$os = Get-CimInstance Win32_OperatingSystem
$cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
$ndp = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
$netfx = 'unknown'
if ($ndp -ge 533320) { $netfx = '4.8.1' } elseif ($ndp -ge 528040) { $netfx = '4.8' } elseif ($ndp -ge 461808) { $netfx = '4.7.2' }
$sdk = (& dotnet --version | Out-String).Trim()
$pylite = ([regex]::Match((Get-Content -Raw src/PyLite/PyLite.csproj), '<Version>(.*?)</Version>')).Groups[1].Value
$iron = ([regex]::Match((Get-Content -Raw tools/PyLite.DiffHarness/PyLite.DiffHarness.csproj), 'Include="IronPython" Version="(.*?)"')).Groups[1].Value
$xunit = ([regex]::Match((Get-Content -Raw tests/PyLite.Tests/PyLite.Tests.csproj), 'Include="xunit" Version="(.*?)"')).Groups[1].Value
$commit = (& git rev-parse --short HEAD | Out-String).Trim()
if ((& git status --porcelain | Out-String).Trim().Length -gt 0) { $commit = $commit + ' (with local changes)' }
$started = [DateTime]::UtcNow

# ---- build everything once ----
$sw = [Diagnostics.Stopwatch]::StartNew()
& dotnet build PyLite.sln -c Release -nologo -v q | Out-Null
Add-Gate 'build (PyLite.sln, Release)' ($LASTEXITCODE -eq 0) 'warnings as errors in the engine' $sw.Elapsed
if ($LASTEXITCODE -ne 0) { exit 1 }

# ---- xUnit ----
$xunitRows = @()
foreach ($run in @(@{ Label = 'xUnit, everyday (Category!=MachineSensitive)'; Filter = 'Category!=MachineSensitive'; File = 'everyday.trx' },
                   @{ Label = 'xUnit, MachineSensitive'; Filter = 'Category=MachineSensitive'; File = 'machine.trx' }))
{
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & dotnet test tests/PyLite.Tests -c Release --no-build --nologo -v q --filter $run.Filter `
        --results-directory $work --logger ('trx;LogFileName=' + $run.File) | Out-Null
    $code = $LASTEXITCODE
    $c = Read-Trx (Join-Path $work $run.File)
    $result = 'no result file'
    if ($c -ne $null) { $result = '{0} passed, {1} failed of {2}' -f $c.passed, $c.failed, $c.total }
    Add-Gate $run.Label (($code -eq 0) -and ($c -ne $null) -and ($c.failed -eq '0')) $result $sw.Elapsed
}

# ---- conformance ----
$sw = [Diagnostics.Stopwatch]::StartNew()
Push-Location conformance/PyLite.Conformance
$conf = & dotnet run -c Release | Out-String   # dotnet run hands unknown options to the program: no --nologo
$code = $LASTEXITCODE
Pop-Location
$m = [regex]::Match($conf, 'PASS (\d+) / FAIL (\d+) / SKIP (\d+)')
$result = 'no summary line'
if ($m.Success) { $result = '{0} passed, {1} failed, {2} skipped' -f $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value }
Add-Gate 'conformance (CPython Lib/test cases)' (($code -eq 0) -and $m.Success -and ($m.Groups[2].Value -eq '0') -and ([int]$m.Groups[1].Value -gt 0)) $result $sw.Elapsed

# ---- differential ----
$sw = [Diagnostics.Stopwatch]::StartNew()
$diffMd = Join-Path $work 'diff.md'
$diff = & dotnet run --project tools/PyLite.DiffHarness -c Release -- --corpus tools/diff-corpus --fail-on-bug --report md --out $diffMd | Out-String
$code = $LASTEXITCODE
$m = [regex]::Match($diff, '(\d+) scripts \| match=(\d+) known=(\d+) bug=(\d+) engineError=(\d+)')
$result = 'no summary line'
if ($m.Success) { $result = '{0} scripts: {1} match, {2} known difference(s), {3} bug(s)' -f $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value, $m.Groups[4].Value }
Add-Gate ('differential (vs IronPython ' + $iron + ')') (($code -eq 0) -and $m.Success -and ($m.Groups[4].Value -eq '0') -and ($m.Groups[5].Value -eq '0')) $result $sw.Elapsed
$diffRows = @()
if (Test-Path $diffMd) { $diffRows = Get-Content $diffMd | Where-Object { $_ -match '^\| .+\.py \|' } }

# ---- adversarial ----
$sw = [Diagnostics.Stopwatch]::StartNew()
Push-Location tools/PyLite.Stress
$probeNames = Get-ChildItem bombs -Filter *.py | ForEach-Object { $_.BaseName }
$stress = & dotnet run -c Release -- bombs
$code = $LASTEXITCODE
Pop-Location
# Result lines: name (31) | outcome (35) | ms (8) | cpu (8) | steps (11) | allocMB (8) | depth.
$bombRows = @()
foreach ($line in $stress)
{
    if ($line.Length -lt 101) { continue }
    $name = $line.Substring(0, 31).Trim()
    if ($probeNames -notcontains $name) { continue }
    $bombRows += [pscustomobject]@{
        Name = $name; Outcome = $line.Substring(31, 35).Trim(); Ms = $line.Substring(66, 8).Trim();
        Steps = $line.Substring(82, 11).Trim(); AllocMb = $line.Substring(93, 8).Trim()
    }
}
$held = ($stress | Out-String) -match 'sandbox held on every probe'
$result = '{0} probes, {1}' -f $bombRows.Count, $(if ($held) { 'the sandbox held on every one' } else { 'NOT ALL HELD' })
Add-Gate 'adversarial (Stress corpus)' (($code -eq 0) -and $held -and ($bombRows.Count -eq $probeNames.Count)) $result $sw.Elapsed

# ---- the report ----
$finished = [DateTime]::UtcNow
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# Test report')
[void]$sb.AppendLine()
[void]$sb.AppendLine('Written by `tools/run-gates.ps1`; run it to repeat every number below (see `TESTING.md`).')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| | |')
[void]$sb.AppendLine('|---|---|')
[void]$sb.AppendLine('| PyLite | ' + $pylite + ', commit `' + $commit + '` |')
[void]$sb.AppendLine('| run | ' + $started.ToString('yyyy-MM-dd HH:mm') + ' to ' + $finished.ToString('HH:mm') + ' UTC |')
[void]$sb.AppendLine('| OS | ' + $os.Caption.Trim() + ' (' + $os.Version + ') |')
[void]$sb.AppendLine('| CPU | ' + $cpu + ', ' + [Environment]::ProcessorCount + ' logical processors |')
[void]$sb.AppendLine('| .NET | Framework ' + $netfx + ' (release ' + $ndp + '), SDK ' + $sdk + ' |')
[void]$sb.AppendLine('| reference | IronPython ' + $iron + ' (NuGet) |')
[void]$sb.AppendLine('| xUnit | ' + $xunit + ' |')
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Gates')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| gate | result | outcome | time |')
[void]$sb.AppendLine('|---|---|---|---:|')
foreach ($g in $gates)
{
    $mark = 'FAIL'
    if ($g.Ok) { $mark = 'pass' }
    [void]$sb.AppendLine('| ' + $g.Name + ' | ' + $mark + ' | ' + $g.Result + ' | ' + ('{0:N0} s' -f $g.Took.TotalSeconds) + ' |')
}
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Differential: PyLite and IronPython ' + $iron + ' on the same scripts')
[void]$sb.AppendLine()
[void]$sb.AppendLine('Each script sets `result`; the two are compared by `repr` (a `dict` order-free, floats by their bits), or by')
[void]$sb.AppendLine('the exception class when the script raises. A known difference is listed with its reason in')
[void]$sb.AppendLine('`tools/PyLite.DiffHarness/known-differences.txt`.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| script | class | detail |')
[void]$sb.AppendLine('|---|---|---|')
foreach ($r in $diffRows) { [void]$sb.AppendLine($r) }
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Adversarial: which limit stopped each probe')
[void]$sb.AppendLine()
[void]$sb.AppendLine('`ok` and `raise` mean the script finished or raised a Python exception it was meant to raise; `budget X`')
[void]$sb.AppendLine('names the limit that stopped it. A probe that hangs past its deadline or crashes the host would fail the')
[void]$sb.AppendLine('gate. The probes and their limits are in `tools/PyLite.Stress/bombs`; the groups in its README.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| probe | outcome | ms | steps | MB charged |')
[void]$sb.AppendLine('|---|---|---:|---:|---:|')
foreach ($b in $bombRows)
{
    [void]$sb.AppendLine('| `' + $b.Name + '` | ' + ($b.Outcome -replace '\|', '/') + ' | ' + $b.Ms + ' | ' + $b.Steps + ' | ' + $b.AllocMb + ' |')
}
$outPath = $Out
if (-not [IO.Path]::IsPathRooted($outPath)) { $outPath = Join-Path $root $Out }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outPath) | Out-Null
[IO.File]::WriteAllText($outPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
Remove-Item -Recurse -Force $work
Write-Host ''
Write-Host ('report -> ' + $outPath)
if ($failed) { exit 1 }
exit 0
