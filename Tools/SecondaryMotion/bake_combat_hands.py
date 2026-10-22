"""Bake the native 15-joint fighting-stance hand tracks for Jin and Kazuya."""
import hashlib,json,struct,sys
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation as R
sys.dont_write_bytecode=True
PROJECT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(PROJECT/'Tools/CombatPoseConverter/vendor'))
from t7extract.motion import decode_motion
report=[]
for ch,folder,mid,rigfile in [('jin','jin_motion',1442,'JinSource/SK_CH_jin_fac.json'),('kaz','kazuya_motion',1435,'KazuyaSource/kaz_fac.json')]:
    workspace=PROJECT.parents[1]
    move=next(m for m in json.loads((workspace/f'decrypted/{folder}/{ch}/moveset.json').read_text())['moves'] if m['id']==mid)
    raw=(workspace/f'decrypted/tkdata/mothead/mask/{ch}rotsm.arc').read_bytes()
    rig={b['name']:b for b in json.loads((PROJECT/'Assets/Remaster/Editor'/rigfile).read_text())['bones']}
    entries=[];sources=[]
    for side,pid in [('L',0x842e),('R',0x842f)]:
        prop=next(p for p in move['properties'] if p['id']==pid)
        assert prop['value']&0xc000==0x4000
        index=prop['value']&0x3fff
        offset,size=struct.unpack_from('<II',raw,12)
        start=offset+struct.unpack_from('>I',raw,offset+20+index*4)[0]
        motion=decode_motion(raw[start:offset+size],'>')
        assert len(motion.descriptors)==15
        # Compact hand bank contains the three phalanges of each digit;
        # unlike entrance's 19-channel bank it omits the four palm/base joints.
        names=[f'{side}_{digit}{joint}' for digit in ['Thumb','Index','Middle','Ring','Pinky'] for joint in [1,2,3]]
        for i,name in enumerate(names):
            rest=R.from_quat([rig[name]['rotation'][a] for a in 'xyzw'])
            euler=np.array(motion.euler_samples())[:,i]
            q=R.from_euler('xyz',euler).as_quat()
            # The compact hand tracks share a curl sign; the extracted left
            # and right hand bind bases have opposite palm normals.
            native=R.from_quat(np.stack([q[:,0],-q[:,2],q[:,1],q[:,3]],axis=1))
            if side=='L': native=R.from_quat(np.stack([-q[:,0],q[:,2],q[:,1],q[:,3]],axis=1))
            if name.endswith(('Index2','Middle2','Ring2','Pinky2')):
                # Regression guard: phalanges must bend into the palm, never
                # backward toward the knuckles after the native-basis conversion.
                bend=native.apply(np.tile([-1.,0.,0.],(len(q),1)))[:,2]
                assert np.all(bend*(1 if side=='L' else -1)>0),name
            delta=(rest.inv()*native).as_quat()
            entries.append((name,np.concatenate([np.zeros((len(q),3)),delta,np.ones((len(q),3))],axis=1)))
        sources.append(dict(side=side,propertyId=hex(pid),reference=prop['value'],index=index,frames=len(motion.samples)))
    frames=len(motion.samples)
    samples=np.array([v for _,v in entries]).transpose(1,0,2).astype('<f4')
    assert np.isfinite(samples).all() and np.allclose(np.linalg.norm(samples[:,:,3:7],axis=2),1,atol=1e-5)
    data=bytearray(struct.pack('<4sIII',b'SMT1',frames,len(entries),60))
    for name,_ in entries:
        b=name.encode();data+=struct.pack('<I',len(b))+b
    data+=samples.tobytes()
    (PROJECT/f'Assets/Remaster/Resources/SecondaryMotion/{ch}_combat_hands.bytes').write_bytes(data)
    report.append(dict(character=ch,moveId=mid,animation=move['animation'],archiveSha256=hashlib.sha256(raw).hexdigest(),tracks=sources,bones=len(entries),frames=frames))
output=PROJECT/'Validation/CombatHands';output.mkdir(exist_ok=True,parents=True)
(output/'source.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
