#!/usr/bin/env bash
# Build and run one headless battle harness against the project's pure C# battle code.
# Usage: run.sh <Name> [project]   e.g. run.sh Facing
#   Name: Facing | Jitter | Pushed | Ko | Falls | Getup | Props | Trace (a <Name>.cs here)
set -e
name=$1
P=${2:-"D:/games/Tekken.7.Ultimate.Edition.v5.10/UnityRemaster/TekkenVersusRemaster"}
U="C:/Program Files/Unity 2023.2.22f1/Editor/Data"
H=$(cygpath -m "$(cd "$(dirname "$0")" && pwd)")
OUT=$(cygpath -m "${TMPDIR:-/tmp}")/tekken-versus-harness; mkdir -p "$OUT"
R="$U/NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21"
refs=()
for d in "$R"/System*.dll "$R"/Microsoft.*.dll "$R"/netstandard.dll "$R"/mscorlib.dll; do
  case "$d" in *Native*|*DiaSymReader*) ;; *) refs+=("-r:$d");; esac
done
S="$P/Assets/_Project/Scripts/Runtime"
"$U/NetCoreRuntime/dotnet.exe" exec "$U/DotNetSdkRoslyn/csc.dll" -nologo -nostdlib -noconfig -langversion:9 \
  -target:exe -out:"$OUT/$name.dll" "${refs[@]}" "$H/Shim.cs" "$H/$name.cs" \
  "$S/CommandGraph.cs" "$S/CommandInterpreter.cs" "$S/BattleModel.cs" "$S/CpuPlayer.cs" "$S/BattleEffectDemo.cs" | grep -E "error" && exit 1 || true
cp "$H/runtimeconfig.json" "$OUT/$name.runtimeconfig.json"
"$U/NetCoreRuntime/dotnet.exe" "$OUT/$name.dll" "$P"
