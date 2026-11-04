#!/usr/bin/env bash
# Compile Assembly-CSharp and Assembly-CSharp-Editor with Unity's own Roslyn while the editor stays
# open (Unity only compiles when the user focuses it). Reuses the newest Bee response files and
# regenerates their source lists, so added or removed scripts are picked up.
# Usage: compile_check.sh [project]
P=${1:-"D:/games/Tekken.7.Ultimate.Edition.v5.10/UnityRemaster/TekkenVersusRemaster"}
U="C:/Program Files/Unity 2023.2.22f1/Editor/Data"
OUT=$(cygpath -m "${TMPDIR:-/tmp}")/tekken-versus-compile; mkdir -p "$OUT"
cd "$P" || exit 1
status=0
for a in Assembly-CSharp Assembly-CSharp-Editor; do
  # The newest editor build's response file: a player build's (no UNITY_EDITOR define) would
  # hide editor-only members and report false errors.
  src=$(ls -t Library/Bee/artifacts/*.dag/$a.rsp | while read f; do grep -q -- '-define:UNITY_EDITOR$' "$f" && { echo "$f"; break; }; done)
  dir=$([ $a = Assembly-CSharp ] && echo Scripts/Runtime || echo Scripts/Editor)
  { grep -v -E '^-out:|^-refout:|\.cs"$' "$src" | sed "s#^-r:\"Library/Bee/artifacts/[^\"]*/Assembly-CSharp.ref.dll\"#-r:\"$OUT/Assembly-CSharp.dll\"#"
    # Built-in modules listed in the manifest but absent from Unity's last response file (Unity adds
    # them on its next package resolve), e.g. com.unity.modules.particlesystem -> ParticleSystemModule.
    for m in $(grep -o 'com\.unity\.modules\.[a-z]*' Packages/manifest.json); do
      dll=$(ls "$U/Managed/UnityEngine/" | grep -i "^UnityEngine\.${m#com.unity.modules.}Module\.dll$" | head -1)
      [ -n "$dll" ] && ! grep -q "$dll" "$src" && echo "-r:\"$U/Managed/UnityEngine/$dll\""
    done
    find "Assets/_Project/$dir" -name '*.cs' | sed 's/.*/"&"/'
    echo "-out:$OUT/$a.dll"; } > "$OUT/$a.rsp"
  "$U/NetCoreRuntime/dotnet.exe" exec "$U/DotNetSdkRoslyn/csc.dll" @"$OUT/$a.rsp" 2>&1 | grep -E ' error ' | head -20
  code=${PIPESTATUS[0]}; echo "$a exit $code"; [ $code -ne 0 ] && status=1
done
exit $status
