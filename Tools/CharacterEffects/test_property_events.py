"""Compile the real battle sources with the existing local headless shim and test visual events."""
from pathlib import Path
import argparse
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--project', type=Path, default=Path(__file__).resolve().parents[2])
parser.add_argument('--unity', type=Path, default=Path('C:/Program Files/Unity 2023.2.22f1/Editor/Data'))
parser.add_argument('--harness', type=Path, default=Path.home() / '.claude/skills/tekken-versus-battle/harness')
args = parser.parse_args()
out = args.project / 'Validation/JinEffects/PropertyEvents'
out.mkdir(parents=True, exist_ok=True)
runtime = args.unity / 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
refs = sorted(p for p in runtime.glob('*.dll') if p.name.startswith(('System', 'Microsoft.')) or p.name in ('netstandard.dll', 'mscorlib.dll'))
refs = [p for p in refs if 'Native' not in p.name and 'DiaSymReader' not in p.name]
sources = [args.harness / 'Shim.cs', Path(__file__).with_name('PropertyEvents.cs')]
sources += [args.project / 'Assets/_Project/Scripts/Runtime' / (n + '.cs') for n in ('CommandGraph', 'CommandInterpreter', 'BattleModel', 'CpuPlayer')]
dll = out / 'PropertyEvents.dll'
rsp = out / 'compile.rsp'
rsp.write_text('\n'.join(['-nologo', '-nostdlib', '-noconfig', '-langversion:9', '-target:exe', f'-out:"{dll}"'] + [f'-r:"{p}"' for p in refs] + [f'"{p}"' for p in sources]), encoding='utf-8')
dotnet = args.unity / 'NetCoreRuntime/dotnet.exe'
subprocess.run([str(dotnet), 'exec', str(args.unity / 'DotNetSdkRoslyn/csc.dll'), '@' + str(rsp)], check=True)
(out / 'PropertyEvents.runtimeconfig.json').write_bytes((args.harness / 'runtimeconfig.json').read_bytes())
result = subprocess.run([str(dotnet), str(dll), str(args.project)], check=True, text=True, capture_output=True)
(out / 'result.txt').write_text(result.stdout, encoding='utf-8')
print(result.stdout, end='')
