param(
    [string]$UnityData = 'C:/Program Files/Unity 2023.2.22f1/Editor/Data',
    [string]$Project = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)
$ErrorActionPreference = 'Stop'
$runtimeRoot = Join-Path $UnityData 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$dotnet = Join-Path $UnityData 'NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll'
$outputRoot = Join-Path $Project 'Validation/Arcade'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$outputAssembly = Join-Path $outputRoot 'ArcadeCoreChecks.dll'
$arguments = @('-nologo', '-nostdlib', '-noconfig', '-langversion:9', '-target:exe', "-out:$outputAssembly")
$references = Get-ChildItem -LiteralPath $runtimeRoot -Filter '*.dll' | Where-Object {
    $_.Name -match '^(System|Microsoft\.|netstandard|mscorlib)' -and $_.Name -notmatch 'Native|DiaSymReader'
}
$arguments += @($references | ForEach-Object { '-r:' + $_.FullName })
$arguments += @('ArcadeSettings.cs','ArcadeStorage.cs','ArcadeLedger.cs','ArcadeInput.cs' | ForEach-Object { Join-Path (Join-Path $Project 'Assets/_Project/Scripts/Runtime/Arcade') $_ })
$arguments += Join-Path $PSScriptRoot 'CoreChecks.cs'
& $dotnet exec $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Arcade harness compilation failed: $LASTEXITCODE" }
$runtimeConfig = '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.21"}}}'
[IO.File]::WriteAllText((Join-Path $outputRoot 'ArcadeCoreChecks.runtimeconfig.json'), $runtimeConfig)
& $dotnet $outputAssembly $outputRoot
if ($LASTEXITCODE -ne 0) { throw "Arcade harness failed: $LASTEXITCODE" }
