"""Bake native entrance hand/face channels into bind-relative Unity transforms."""
import ast,json,struct,sys
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation as R
PROJECT=Path(__file__).resolve().parents[2]
sys.dont_write_bytecode=True
sys.path.insert(0,str(PROJECT/'Tools/CombatPoseConverter/vendor'))
from t7extract.motion import decode_motion

helper=ast.parse((PROJECT.parent/'AnimationResearch/Direct_TekkenAnimHelper.py').read_text())
def returned(name):
 fn=next(n for n in helper.body if isinstance(n,ast.FunctionDef) and n.name==name)
 return next(n for n in fn.body if isinstance(n,ast.Return)).value.elts
hand_names={}
for side,fun in [('L','getLeftHandAnimFrameFromBones'),('R','getRightHandAnimFrameFromBones')]:
 hand_names[side]=[side+'_'+n.id.rsplit('_',1)[0] for n in returned(fun)[::3]]
face_names=[n.value.slice.value for n in returned('getFaceAnimFrameFromBones')[::6] if isinstance(n,ast.Subscript)]
face_names+=['Bero2_Joint','Bero3_Joint']
assert len(face_names)==43

def bake():
 output=PROJECT/'Assets/Remaster/Resources/SecondaryMotion';output.mkdir(parents=True,exist_ok=True)
 report=[]
 for ch,mid,source in [('jin',2220,'JinSource/SK_CH_jin_fac.json'),('kaz',2326,'KazuyaSource/kaz_fac.json')]:
  rig={b['name']:b for b in json.loads((PROJECT/'Assets/Remaster/Editor'/source).read_text())['bones']}
  entries=[];frames=None
  for kind,side in [('left_hand','L'),('right_hand','R'),('face',None)]:
   motion=decode_motion((PROJECT/f'Validation/SecondaryMotion/{ch}_{mid}_{kind}.c8').read_bytes())
   frames=len(motion.samples);names=face_names if side is None else hand_names[side]
   for i,name in enumerate(names):
    assert name in rig,name
    rest=rig[name];qrest=R.from_quat([rest['rotation'][a] for a in 'xyzw']);prest=np.array([rest['position'][a] for a in 'xyz'])
    result=[]
    for f in motion.samples:
     euler=f[2*i+1] if side is None else f[i]
     q=R.from_euler('xyz',euler).as_quat()
     # Native local axes -> extracted Unity skeleton: reflection (-X,+Z,-Y).
     native=R.from_quat([q[0],-q[2],q[1],q[3]])
     delta=(qrest.inv()*native).as_quat()
     pos=np.array([-f[2*i][0],f[2*i][2],-f[2*i][1]])*.001-prest if side is None else np.zeros(3)
     scale=np.ones(3)
     if side is None and name in ['Bero1_Joint','Bero1_Joint_pos','Bero2_Joint','Bero3_Joint']:
      channel=86+['Bero1_Joint','Bero1_Joint_pos','Bero2_Joint','Bero3_Joint'].index(name)
      scale=np.array(f[channel])
     result.append(np.r_[pos,delta,scale])
    entries.append((name,result))
  data=bytearray(struct.pack('<4sIII',b'SMT1',frames,len(entries),60))
  for name,_ in entries:
   raw=name.encode();data+=struct.pack('<I',len(raw))+raw
  samples=np.array([e[1] for e in entries]).transpose(1,0,2).astype('<f4')
  assert np.isfinite(samples).all()
  data+=samples.tobytes()
  (output/f'{ch}_{mid}.bytes').write_bytes(data)
  report.append(dict(character=ch,frames=frames,bones=len(entries),maxPositionDelta=float(abs(samples[:,:,:3]).max()),scaleRange=[float(samples[:,:,7:].min()),float(samples[:,:,7:].max())]))
 (PROJECT/'Validation/SecondaryMotion/bake-check.json').write_text(json.dumps(report,indent=2)+'\n')
 print(report)
if __name__=='__main__':bake()
