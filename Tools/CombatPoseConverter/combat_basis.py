"""Transfer decoded C8 body poses through the established reference rig."""
import json
from pathlib import Path
from types import SimpleNamespace
import numpy as np
from scipy.spatial.transform import Rotation as R
from pose_basis.reference_math import applyRotationFromAnimdata

folder=Path(__file__).resolve().parent/'pose_basis'
rig={b['name']:b for b in json.loads((folder/'reference-rig.json').read_text())}
rest={n:np.array(b['matrix']).reshape(4,4).T for n,b in rig.items()}
target=json.loads((folder/'target-rig.json').read_text())
names=[b['name'] for b in target]
local=[R.from_quat([b['rotation'][a] for a in 'xyzw']).as_matrix() for b in target]
world=[]
for i,b in enumerate(target):
    world.append(local[i] if b['parent']<0 else world[b['parent']]@local[i])
modes={1:'xyz',2:'xzy',3:'yxz',4:'yzx',5:'zxy',6:'zyx'}
align=np.diag([-1.,1.,1.])

def retarget(sample,bones,heading_degrees=90):
    source={n:SimpleNamespace(rotation_euler=SimpleNamespace(x=0.,y=0.,z=0.),
                             location=SimpleNamespace(x=0.,y=0.,z=0.)) for n in rig}
    applyRotationFromAnimdata(SimpleNamespace(pose=SimpleNamespace(bones=source)),np.asarray(sample).reshape(-1))
    posed={}
    def pose(name):
        if name in posed:return posed[name]
        b=rig[name];parent=b['parent']
        basis=rest[name] if parent is None else np.linalg.inv(rest[parent])@rest[name]
        delta=np.eye(4);e=source[name].rotation_euler;order=modes.get(b['mode'],'xyz')
        delta[:3,:3]=R.from_euler(order,[getattr(e,a) for a in order]).as_matrix()
        p=source[name].location;delta[:3,3]=[p.x,p.y,p.z]
        posed[name]=(np.eye(4) if parent is None else pose(parent))@basis@delta
        return posed[name]
    heading=R.from_euler('y',heading_degrees,degrees=True).as_matrix()
    rotations={n:heading@align@pose(n)[:3,:3]@rest[n][:3,:3].T@align.T@world[names.index(n)] for n in bones}
    target_world=[];quats={}
    for i,b in enumerate(target):
        parent=np.eye(3) if b['parent']<0 else target_world[b['parent']]
        w=rotations.get(b['name'],parent@local[i]);target_world.append(w)
        if b['name'] in rotations:quats[b['name']]=R.from_matrix(parent.T@w).as_quat()
    parent=world[target[names.index('BASE')]['parent']]
    root=parent.T@heading@align@pose('BASE')[:3,3]
    return np.r_[root,np.array([quats[n] for n in bones]).reshape(-1)]
