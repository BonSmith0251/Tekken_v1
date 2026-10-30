param(
    [string]$UnityData = 'C:/Program Files/Unity 2023.2.22f1/Editor/Data',
    [string]$Project = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [switch]$CaptureLegacyBaseline,
    [string]$LegacyCpuSource,
    [switch]$FlowChecks,
    [switch]$OperatorChecks,
    [switch]$RankingChecks
)
$ErrorActionPreference = 'Stop'
$runtimeRoot = Join-Path $UnityData 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$dotnet = Join-Path $UnityData 'NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll'
$outputRoot = Join-Path $Project 'Validation/Arcade'
$sourceRoot = Join-Path $Project 'Assets/_Project/Scripts/Runtime'
$baseline = Join-Path $PSScriptRoot 'legacy-cpu-baseline.json'
$name = if ($CaptureLegacyBaseline) { 'LegacyCpuCapture' } elseif ($RankingChecks) { 'ArcadeRankingChecks' } elseif ($OperatorChecks) { 'ArcadeOperatorChecks' } elseif ($FlowChecks) { 'ArcadeFlowChecks' } else { 'ArcadeMatchChecks' }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$outputAssembly = Join-Path $outputRoot "$name.dll"
$arguments = @('-nologo', '-nostdlib', '-noconfig', '-langversion:9', '-target:exe', "-out:$outputAssembly")
$arguments += @(Get-ChildItem -LiteralPath $runtimeRoot -Filter '*.dll' | Where-Object {
    $_.Name -match '^(System|Microsoft\.|netstandard|mscorlib)' -and $_.Name -notmatch 'Native|DiaSymReader'
} | ForEach-Object { '-r:' + $_.FullName })
$arguments += @('CommandGraph.cs', 'CommandInterpreter.cs', 'BattleModel.cs' | ForEach-Object { Join-Path $sourceRoot $_ })
$arguments += Join-Path $PSScriptRoot 'BattleShim.cs'
$arguments += Join-Path $PSScriptRoot 'LegacyReplay.cs'
if ($CaptureLegacyBaseline) {
    if (-not $LegacyCpuSource -or -not (Test-Path -LiteralPath $LegacyCpuSource)) { throw 'Supply the backed-up pre-change CpuPlayer.cs.' }
    if (Test-Path -LiteralPath $baseline) { throw 'Baseline already exists; do not overwrite regression evidence.' }
    $arguments += @('-define:LEGACY_CAPTURE', (Resolve-Path -LiteralPath $LegacyCpuSource).Path)
} else {
    $arguments += Join-Path $sourceRoot 'CpuPlayer.cs'
    $arguments += @((Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Arcade') -Filter '*.cs').FullName)
    $arguments += Join-Path $PSScriptRoot $(if ($RankingChecks) { 'RankingChecks.cs' } elseif ($OperatorChecks) { 'OperatorChecks.cs' } elseif ($FlowChecks) { 'FlowChecks.cs' } else { 'MatchChecks.cs' })
}
& $dotnet exec $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Match harness compilation failed: $LASTEXITCODE" }
[IO.File]::WriteAllText((Join-Path $outputRoot "$name.runtimeconfig.json"), '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.21"}}}')
& $dotnet $outputAssembly $Project $baseline $outputRoot
if ($LASTEXITCODE -ne 0) { throw "Match harness failed: $LASTEXITCODE" }
