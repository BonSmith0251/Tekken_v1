"""Tekken body-pose converter with corrected spherical combat rotations.

Requires numpy, scipy, and the workspace's t7extract package. Profiles encode
the reference rig's local bone bases; optional calibration aligns a source pose
to a target pose. Original-game visual comparison remains a separate check.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys
import numpy as np
from scipy.spatial.transform import Rotation

sys.path.insert(0,str(Path(__file__).resolve().parent.parent/'t7extract'))
sys.path.insert(0,str(Path(__file__).resolve().parent/'vendor'))
from t7extract.motion import decode_motion

BONES=['BASE','Spine1','Hip','Neck','Head','R_Shoulder','R_Arm','R_ForeArm','R_Hand',
       'L_Shoulder','L_Arm','L_ForeArm','L_Hand','R_UpLeg','R_Leg','R_Foot','L_UpLeg','L_Leg','L_Foot']
CHANNELS=[3,4,5,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22]
AXES=np.array([[-1.,0,0],[0,0,1],[0,-1,0]])
STRIDE=3+4*len(BONES)

def digest(data):return hashlib.sha256(data).hexdigest()

def read_motion(path,endian='auto'):
    data=Path(path).read_bytes()
    if endian=='auto':
        if data[:2] in [b'\x64\0',b'\xc8\0']:endian='<'
        elif data[:2] in [b'\0\x64',b'\0\xc8']:endian='>'
        else:raise ValueError('Not a native 64/C8 animation')
    motion=decode_motion(data,endian)
    if motion.format=='0x64':
        motion=decode_motion(motion.to_c8())
    if len(motion.descriptors)!=23:raise ValueError('Only 23-channel body motion is supported')
    return motion,data

def read_jrt(path):
    data=Path(path).read_bytes()
    if len(data)<16:raise ValueError('Truncated JRT header')
    magic,frames,bones,fps=struct.unpack_from('<4sIII',data)
    if magic!=b'JRT1' or bones!=len(BONES) or fps!=60 or frames<1 or len(data)!=16+frames*STRIDE*4:
        raise ValueError('Invalid JRT body clip')
    values=np.frombuffer(data,dtype='<f4',offset=16).reshape(frames,STRIDE).astype(float)
    if not np.isfinite(values).all():raise ValueError('Non-finite target pose')
    norms=np.linalg.norm(values[:,3:].reshape(-1,4),axis=1)
    if np.max(abs(norms-1))>1e-4:raise ValueError('Target contains non-unit quaternions')
    return values,data

def quaternion(value):
    q=np.asarray(value,dtype=float)
    if q.shape!=(4,) or not np.isfinite(q).all() or abs(np.linalg.norm(q)-1)>1e-4:
        raise ValueError('Expected a finite unit XYZW quaternion')
    return Rotation.from_quat(q)

def validate_profile(profile):
    if profile.get('schema')!='jin-combat-pose/1':raise ValueError('Unsupported profile schema')
    if profile.get('fidelity')!='experimental-calibration':
        raise ValueError('This converter only supports explicitly experimental profiles')
    mappings=profile['bones']
    if len(mappings)!=len(BONES) or [b['name'] for b in mappings]!=BONES:
        raise ValueError('Profile must map all 19 body bones exactly once, in JRT order')
    if sorted(b['channel'] for b in mappings)!=sorted(CHANNELS):raise ValueError('Invalid or duplicate source channels')
    for b in mappings:
        if b['order'] not in ['xyz','xzy','yxz','yzx','zxy','zyx','XYZ','XZY','YXZ','YZX','ZXY','ZYX']:
            raise ValueError('Invalid Euler order')
        axes=np.asarray(b['axes'],float)
        if axes.shape!=(3,3) or not np.isfinite(axes).all() or not np.allclose(axes.T@axes,np.eye(3),atol=1e-6):
            raise ValueError('Bone axes must be an orthogonal coordinate conversion')
        quaternion(b['pre']);quaternion(b['post'])
    root=profile['root']
    if profile.get('sourceSpace') not in [None,'jin-reference-v3']:
        raise ValueError('Unsupported source pose space')
    if not np.isfinite(float(profile.get('headingDegrees',90))):
        raise ValueError('Invalid heading')
    for name in ['sourceReference','targetReference']:
        v=np.asarray(root[name],float)
        if v.shape!=(3,) or not np.isfinite(v).all():raise ValueError('Invalid root reference')
    if not 0<float(root['metersPerUnit'])<=1:raise ValueError('Invalid unit scale')

def source_rotation(sample,bone):
    v=np.asarray(sample[bone['channel']])
    # Angles are named x/y/z; Euler order changes multiplication, not channels.
    order=bone['order']
    values=[v['xyz'.index(a.lower())] for a in order]
    raw=Rotation.from_euler(order,values).as_matrix()
    axes=np.asarray(bone['axes'])
    return Rotation.from_matrix(axes@raw@axes.T)

def root_sample(sample):
    return np.asarray(sample[1])+np.asarray(sample[0])*[1,0,-1]

def calibrate(source_path,target_path,source_frame=0,target_frame=0,order='xyz'):
    if order!='xyz':
        raise ValueError('Version 3 uses the reference skeleton Euler modes; custom global Euler order is not supported.')
    source,source_bytes=read_motion(source_path)
    target,target_bytes=read_jrt(target_path)
    if not 0<=source_frame<len(source.samples) or not 0<=target_frame<len(target):
        raise ValueError('Reference frame is out of range')
    from combat_basis import retarget
    sample=source.samples[source_frame];pose=target[target_frame]
    mapped=retarget(sample,BONES)
    mappings=[]
    for i,(name,channel) in enumerate(zip(BONES,CHANNELS)):
        bone=dict(name=name,channel=channel,order=order,axes=AXES.tolist(),post=[0,0,0,1])
        bone['pre']=(quaternion(pose[3+4*i:7+4*i])*quaternion(mapped[3+4*i:7+4*i]).inv()).as_quat().tolist()
        mappings.append(bone)
    profile=dict(schema='jin-combat-pose/1',fidelity='experimental-calibration',
        sourceSpace='jin-reference-v3',headingDegrees=90,
        notes='Spherical axis-angle decoded before reference-rig retargeting. Optional one-pose alignment.',
        sourceReference=dict(path=str(source_path),sha256=digest(source_bytes),frame=source_frame),
        targetReference=dict(path=str(target_path),sha256=digest(target_bytes),frame=target_frame),
        bones=mappings,root=dict(sourceReference=mapped[:3].tolist(),
            targetReference=pose[:3].tolist(),metersPerUnit=1))
    validate_profile(profile)
    return profile

def default_profile():
    """Use the corrected rig mapping directly, without imposing another stance."""
    return dict(schema='jin-combat-pose/1',fidelity='experimental-calibration',
        sourceSpace='jin-reference-v3',headingDegrees=90,
        notes='Native spherical axis-angle -> C8 Euler -> reference rig -> Jin. No one-pose fitting.',
        bones=[dict(name=n,channel=c,order='xyz',axes=AXES.tolist(),
                    pre=[0,0,0,1],post=[0,0,0,1]) for n,c in zip(BONES,CHANNELS)],
        root=dict(sourceReference=[0,0,0],targetReference=[0,0,0],metersPerUnit=1))

def convert(motion,profile,root_motion='in-place'):
    validate_profile(profile)
    if len(motion.descriptors)!=23:raise ValueError('Only 23-channel body motion is supported')
    if root_motion not in ['in-place','source']:raise ValueError('Invalid root-motion mode')
    result=[];previous=None
    reference=np.asarray(profile['root']['sourceReference'])
    target=np.asarray(profile['root']['targetReference'])
    pre=[quaternion(b['pre']) for b in profile['bones']]
    post=[quaternion(b['post']) for b in profile['bones']]
    for sample in motion.samples:
        if profile.get('sourceSpace')=='jin-reference-v3':
            from combat_basis import retarget
            mapped=retarget(sample,BONES,profile.get('headingDegrees',90))
            position=target+(mapped[:3]-reference)*profile['root']['metersPerUnit']
            rotations=[quaternion(mapped[3+4*i:7+4*i]) for i in range(len(BONES))]
        else:
            position=target+AXES@(root_sample(sample)-reference)*profile['root']['metersPerUnit']
            rotations=[source_rotation(sample,b) for b in profile['bones']]
        if root_motion=='in-place':position[:2]=target[:2] # BASE local Z is vertical
        q=np.array([(pre[i]*rotations[i]*post[i]).as_quat() for i in range(len(BONES))])
        if previous is not None:q[np.sum(previous*q,axis=1)<0]*=-1
        previous=q
        result.append(np.r_[position,q.reshape(-1)])
    values=np.asarray(result)
    if not np.isfinite(values).all():raise ValueError('Non-finite converted pose')
    return values

def write_clip(path,values,metadata,force=False):
    values=np.asarray(values,dtype=float)
    if values.ndim!=2 or values.shape[1]!=STRIDE or len(values)<1 or not np.isfinite(values).all():
        raise ValueError('Invalid converted pose array')
    path=Path(path);report_path=path.with_suffix('.conversion.json')
    if not force and (path.exists() or report_path.exists()):
        raise FileExistsError('Output already exists; use --force to replace it')
    path.parent.mkdir(parents=True,exist_ok=True)
    data=struct.pack('<4sIII',b'JRT1',len(values),len(BONES),60)+np.asarray(values,dtype='<f4').tobytes()
    path.write_bytes(data)
    q=values[:,3:].reshape(len(values),len(BONES),4)
    steps=2*np.arccos(np.clip(abs(np.sum(q[1:]*q[:-1],axis=2)),0,1))
    metadata.update(schema='jin-combat-conversion/1',fidelity='experimental-calibration',
        originalGameVerified=False,frames=len(values),bones=BONES,sha256=digest(data),
        maxFrameRotationDegrees=float(np.degrees(steps).max()) if steps.size else 0)
    metadata['warnings']=['Retargeted motion still needs visual comparison with original gameplay.']
    if metadata['maxFrameRotationDegrees']>45:
        metadata['warnings'].append('Large one-frame rotation detected. Do not use for gameplay without review.')
    report_path.write_text(json.dumps(metadata,indent=2))
    return metadata

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    commands=parser.add_subparsers(dest='command',required=True)
    cal=commands.add_parser('calibrate',help='Align one original pose to an explicit target JRT pose')
    cal.add_argument('--source',required=True);cal.add_argument('--target',required=True)
    cal.add_argument('--output',required=True);cal.add_argument('--source-frame',type=int,default=0)
    cal.add_argument('--target-frame',type=int,default=0);cal.add_argument('--order',default='xyz')
    cal.add_argument('--force',action='store_true')
    conv=commands.add_parser('convert',help='Convert original 64/C8 samples with an explicit calibration')
    conv.add_argument('--input',required=True);conv.add_argument('--profile',required=True)
    conv.add_argument('--output',required=True);conv.add_argument('--root-motion',choices=['in-place','source'],default='in-place')
    conv.add_argument('--force',action='store_true')
    args=parser.parse_args()
    if args.command=='calibrate':
        path=Path(args.output)
        if path.exists() and not args.force:raise FileExistsError('Profile exists; use --force')
        p=calibrate(args.source,args.target,args.source_frame,args.target_frame,args.order)
        path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(p,indent=2))
        print('Created experimental calibration:',path)
    else:
        profile_bytes=Path(args.profile).read_bytes();profile=json.loads(profile_bytes)
        if profile.get('sourceSpace')!='jin-reference-v3':
            raise ValueError('This profile predates the spherical rotation fix. Recreate the calibration or use the updated bundled profile.')
        motion,data=read_motion(args.input)
        values=convert(motion,profile,args.root_motion)
        report=write_clip(args.output,values,dict(source=str(args.input),sourceSha256=digest(data),
            profileSha256=digest(profile_bytes),rootMotion=args.root_motion),args.force)
        print(json.dumps({k:report[k] for k in ['frames','fidelity','originalGameVerified','maxFrameRotationDegrees']}))

if __name__=='__main__':
    try:main()
    except (ValueError,KeyError,OSError) as error:
        print('Conversion failed:',error,file=sys.stderr);sys.exit(1)
