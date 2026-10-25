"""Restore Kazuya's original secondary-bone weights, retaining body helper remapping."""
import ast,json,sys
from pathlib import Path
PROJECT=Path(__file__).resolve().parents[2];WORKSPACE=PROJECT.parents[1]
sys.dont_write_bytecode=True
sys.path.insert(0,str(WORKSPACE/'t7extract'))
from t7extract.cli import Source
from t7extract.pak import TEKKEN_ASSET_KEY

code=ast.parse((PROJECT.parent/'export_jin_rig.py').read_text())
nodes=[n for n in code.body if isinstance(n,(ast.Import,ast.ImportFrom)) or isinstance(n,ast.FunctionDef) and n.name in ('vec','decode')]
scope={};exec(compile(ast.Module(body=nodes,type_ignores=[]),'mesh_decoder','exec'),scope)
from bake_entrance_tracks import hand_names,face_names
keep=set(hand_names['L']+hand_names['R']+face_names)
keep.update(['BASE','Spine1','Hip','Neck','Head','R_Shoulder','R_Arm','R_ForeArm','R_Hand','L_Shoulder','L_Arm','L_ForeArm','L_Hand','R_UpLeg','R_Leg','R_Foot','L_UpLeg','L_Leg','L_Foot'])
keep={n.lower() for n in keep}
manifest=json.loads((PROJECT/'Assets/_Project/Characters/Kazuya/Source/Base/manifest.json').read_text());report=[]
with Source(str(WORKSPACE/'TEKKEN.7'),TEKKEN_ASSET_KEY) as source:
 items={i.path.lower():i for i in source.items()}
 for name,entry in manifest['pieces'].items():
  path=PROJECT/entry['path'];old=json.loads(path.read_text());raw=scope['decode'](items[entry['source'].lower()])
  assert len(raw['vertices'])==len(old['vertices']) and raw['bones']==old['bones']
  remap=[]
  for i,bone in enumerate(raw['bones']):
   p=i
   while p>=0 and raw['bones'][p]['name'].lower() not in keep:p=raw['bones'][p]['parent']
   if p<0:p=next(j for j,b in enumerate(raw["bones"]) if b["name"].lower()=="base")
   remap.append(p)
  for w in raw['weights']:
   combined={}
   for i in range(4):
    p=remap[w['boneIndex'+str(i)]];combined[p]=combined.get(p,0)+w['weight'+str(i)]
   pairs=sorted(combined.items(),key=lambda x:-x[1])[:4];pairs += [(0,0)]*(4-len(pairs))
   for i,(p,value) in enumerate(pairs):w['boneIndex'+str(i)]=p;w['weight'+str(i)]=value
  old['weights']=raw['weights'];path.write_text(json.dumps(old,separators=(',',':')))
  print("Restored",name,flush=True)
  report.append(dict(piece=name,vertices=len(raw['vertices']),restored=True))
(PROJECT/'Validation/SecondaryMotion/weight-restore.json').write_text(json.dumps(report,indent=2)+'\n')
print('Restored',len(report),'Kazuya mesh sources')
