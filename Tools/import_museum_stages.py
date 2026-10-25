"""Import placed original static stage geometry; retain a report of skipped components."""
import hashlib,json,re,struct,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
PROJECT=Path(__file__).resolve().parents[1]
sys.path[:0]=[str(ROOT/'t7extract'),str(PROJECT/'Tools/OriginalLighting')]
from t7extract.cli import Source
from t7extract.pak import TEKKEN_ASSET_KEY
from t7extract.package import Package
from t7extract.mesh import extract_mesh
from extract_stage_lighting import properties

def main():
 out=PROJECT/'Assets/_Project/Stages/Source';out.mkdir(parents=True,exist_ok=True)
 source=Source(str(ROOT/'TEKKEN.7'),TEKKEN_ASSET_KEY)
 inventory={i.path.lower():i for i in source.items()}
 cache={}; stages=[]; failures=[]
 for path,item in inventory.items():
  match=re.search(r'/maps/(stg\d{2}[a-z]?)_geom\.umap$',path)
  if not match:continue
  stage=match[1];p=Package(item.read(),item.path); nodes=[];starts=[]
  for idx,e in enumerate(p.exports,1):
   if e.class_name not in ('StaticMeshComponent','SceneComponent','TekkenPlayerStart'):continue
   try:v,_=properties(p,p.reader(e.serial_offset))
   except Exception as ex:failures.append({'stage':stage,'object':e.object_name,'error':str(ex)});continue
   if e.class_name=='TekkenPlayerStart':continue
   ref=v.get('StaticMesh',{}).get('object',''); mesh=''
   if ref and not v.get('bHiddenInGame',False) and v.get('bVisible',True):
    meshpath=ref.split('.')[0].replace('/Game/','TekkenGame/Content/')+'.uasset'
    if meshpath.lower() not in cache:
     try:
      mp=Package(inventory[meshpath.lower()].read(),meshpath)
      mi=next(i for i,x in enumerate(mp.exports) if x.class_name=='StaticMesh')
      m=extract_mesh(mp,mi); key=hashlib.sha256(meshpath.encode()).hexdigest()[:20]
      with (out/(key+'.bytes')).open('wb') as f:
       f.write(struct.pack('<4sii',b'STG1',len(m['positions']),len(m['indices'])))
       for x,y,z in m['positions']:f.write(struct.pack('<3f',x*.01,z*.01,-y*.01))
       for i in m['indices']:f.write(struct.pack('<i',i))
      cache[meshpath.lower()]=key
     except Exception as ex:
      cache[meshpath.lower()]='';failures.append({'stage':stage,'mesh':meshpath,'error':str(ex)})
    mesh=cache[meshpath.lower()]
   nodes.append({'id':idx,'parent':v.get('AttachParent',{}).get('index',0),'name':e.object_name,'mesh':mesh,
    'position':v.get('RelativeLocation',{'x':0,'y':0,'z':0}),
    'rotation':v.get('RelativeRotation',{'pitch':0,'yaw':0,'roll':0}),
    'scale':v.get('RelativeScale3D',{'x':1,'y':1,'z':1})})
  count=sum(bool(n['mesh']) for n in nodes)
  if count:stages.append({'name':stage,'nodes':nodes})
  print(stage,count,'placed meshes',flush=True)
 (out/'stages.json').write_text(json.dumps({'stages':stages}),encoding='utf-8')
 report={'stages':len(stages),'uniqueMeshes':sum(bool(x) for x in cache.values()),'failures':failures,'materials':'neutral geometry preview; original materials and effects not imported'}
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print('DONE',report['stages'],report['uniqueMeshes'],'failures',len(failures),flush=True)
if __name__=='__main__':main()
