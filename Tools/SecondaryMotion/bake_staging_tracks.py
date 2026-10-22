"""Export every native Jin/Kazuya staging hand, face and camera track."""
import json,re,struct,sys
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation as R
sys.dont_write_bytecode=True
from bake_entrance_tracks import PROJECT,hand_names,face_names,hand_rotation
from t7extract.motion import decode_motion
workspace=PROJECT.parents[1]
entries=[]
for ch,folder,rigpath in [('jin','jin_motion','JinSource/SK_CH_jin_fac.json'),('kaz','kazuya_motion','KazuyaSource/kaz_fac.json')]:
    moves=json.loads((workspace/f'decrypted/{folder}/{ch}/moveset.json').read_text())['moves']
    raw=(workspace/f'decrypted/tkdata/mothead/mask/{ch}rotsm.arc').read_bytes()
    rig={b['name']:b for b in json.loads((PROJECT/'Assets/Remaster/Editor'/rigpath).read_text())['bones']}
    seen=set()
    def track(reference,bank):
        assert reference&0xc000==0x4000
        index=reference&0x3fff;offset,size=struct.unpack_from('<II',raw,4+bank*8)
        assert raw[offset:offset+4]==b'MOTA'
        count=struct.unpack_from('>I',raw,offset+12)[0];assert index<count
        start=offset+struct.unpack_from('>I',raw,offset+20+index*4)[0]
        return raw[start:offset+size],index
    for move in moves:
        anim=move.get('animation','')
        if not re.match(r'^(jz|kz)_[sw]\d\d(?:_|\(|$)',anim,re.I) or anim in seen:continue
        seen.add(anim)
        mid=move['id'];frames=move['anim']['frames'];props={p['id']:p['value'] for p in move.get('properties',[])}
        kind='entrance' if '_s' in anim else 'victory'
        # The synchronized y variant has no independent camera/face event. Keep
        # those absent rather than claiming a different take's events are its own.
        resource=f'{ch}_staging_{mid}'
        camera=''
        if 33845 in props:
            data,index=track(props[33845],4)
            fmt,count,n=struct.unpack_from('>HHI',data);assert (fmt,count,n)==(0xc8,4,frames)
            values=struct.unpack_from('>'+str(frames*12)+'f',data,24)
            camera=resource
            (PROJECT/f'Assets/Remaster/Resources/EntranceCameras/{resource}.bytes').write_bytes(struct.pack('<4sIIII',b'T7C1',frames,60,mid,index)+struct.pack('<'+str(len(values))+'f',*values))
        bones=[]
        for pid,bank,side in [(33838,1,'L'),(33839,1,'R'),(33986,2,None)]:
            if pid not in props:continue
            data,index=track(props[pid],bank);motion=decode_motion(data,'>');samples=motion.euler_samples()
            names=face_names if side is None else hand_names[side]
            assert len(samples)==frames
            assert len(motion.descriptors)==(90 if side is None else 19)
            for i,name in enumerate(names):
                rest=rig[name];qrest=R.from_quat([rest['rotation'][a] for a in 'xyzw']);prest=np.array([rest['position'][a] for a in 'xyz'])
                values=[];palm_bends=[];thumb_offsets=[]
                for f in samples:
                    e=f[2*i+1] if side is None else f[i];q=R.from_euler('xyz',e).as_quat()
                    native=hand_rotation(e,side) if side else R.from_quat([q[0],-q[2],q[1],q[3]])
                    delta=(qrest.inv()*native).as_quat()
                    if side=='L' and name in ('L_Index2','L_Middle2','L_Ring2','L_Pinky2'):
                        palm_bends.append(native.apply([-1.,0.,0.])[2])
                    if name=='L_Thumb1':thumb_offsets.append(np.rad2deg((qrest.inv()*native).magnitude()))
                    pos=np.array([-f[2*i][0],f[2*i][2],-f[2*i][1]])*.001-prest if side is None else np.zeros(3)
                    scale=np.ones(3)
                    if side is None and name in ['Bero1_Joint','Bero1_Joint_pos','Bero2_Joint','Bero3_Joint']:
                        scale=np.array(f[86+['Bero1_Joint','Bero1_Joint_pos','Bero2_Joint','Bero3_Joint'].index(name)])
                    values.append(np.r_[pos,delta,scale])
                if palm_bends:assert min(palm_bends)>-.15 and max(palm_bends)>=0,(ch,mid,name,'backward curl')
                if thumb_offsets:assert max(thumb_offsets)<80,(ch,mid,name,'thumb opposition')
                bones.append((name,values))
        payload=bytearray(struct.pack('<4sIII',b'SMT1',frames,len(bones),60))
        for name,_ in bones:
            b=name.encode();payload+=struct.pack('<I',len(b))+b
        samples=np.array([v for _,v in bones]).transpose(1,0,2).astype('<f4');assert np.isfinite(samples).all()
        payload+=samples.tobytes()
        (PROJECT/f'Assets/Remaster/Resources/SecondaryMotion/{resource}.bytes').write_bytes(payload)
        entries.append(dict(character=ch,moveId=mid,animation=anim,kind=kind,frames=frames,camera=camera,secondary=resource,landingStitch=ch=='kaz' and mid==2326))
# Xiaoyu's tracks are baked by her own importer; keep her catalog entries.
for e in json.loads((PROJECT/'Assets/Remaster/Editor/XiaoyuSource/staging.json').read_text())['entries']:
    entries.append(dict(character='xia',moveId=e['moveId'],animation=e['animation'],kind=e['kind'],frames=e['frames'],camera=e['camera'],secondary=e['secondary'],landingStitch=False))
output=PROJECT/'Assets/Remaster/Resources/StagingCatalog.json';output.write_text(json.dumps(dict(entries=entries),indent=2)+'\n')
print(json.dumps(entries,indent=2))
