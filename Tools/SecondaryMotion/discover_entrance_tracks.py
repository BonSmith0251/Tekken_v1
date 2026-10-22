"""Find and decode entrance hand/facial tracks without changing Unity playback."""
import argparse
import ast
import hashlib
import json
from pathlib import Path
import struct
import sys

PROJECT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(PROJECT / 'Tools/CombatPoseConverter/vendor'))
from t7extract.motion import decode_motion

def discover(workspace):
    output = PROJECT / 'Validation/SecondaryMotion'
    output.mkdir(parents=True, exist_ok=True)
    tree = ast.parse((workspace / 'UnityRemaster/AnimationResearch/Direct_TekkenAnimHelper.py').read_text())
    face_function = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'getFaceAnimFrameFromBones')
    face_return = next(n for n in face_function.body if isinstance(n, ast.Return)).value.elts
    labels = [ast.unparse(n) for n in face_return[::3]]
    records = []
    for ch, folder, move_id in [('jin','jin_motion',2220), ('kaz','kazuya_motion',2326)]:
        archive = workspace / f'decrypted/tkdata/mothead/mask/{ch}rotsm.arc'
        data = archive.read_bytes()
        move = next(m for m in json.loads((workspace / f'decrypted/{folder}/{ch}/moveset.json').read_text())['moves'] if m['id'] == move_id)
        for kind, prop_id, bank in [('left_hand',0x842e,1), ('right_hand',0x842f,1), ('face',0x84c2,2)]:
            prop = next(p for p in move['properties'] if p['id'] == prop_id)
            reference = prop['value']
            assert reference & 0xc000 == 0x4000
            index = reference & 0x3fff
            offset,size = struct.unpack_from('<II', data,4+bank*8)
            assert data[offset:offset+4] == b'MOTA'
            count = struct.unpack_from('>I',data,offset+12)[0]
            assert index < count
            start = offset + struct.unpack_from('>I',data,offset+20+index*4)[0]
            motion = decode_motion(data[start:offset+size], '>')
            assert len(motion.samples) == move['anim']['frames']
            assert len(motion.descriptors) == (90 if kind=='face' else 19)
            exported = motion.to_c8()
            decoded = decode_motion(exported)
            assert len(decoded.samples) == len(motion.samples)
            target = output / f'{ch}_{move_id}_{kind}.c8'
            target.write_bytes(exported)
            ranges = [[max(f[c][a] for f in motion.samples)-min(f[c][a] for f in motion.samples) for a in range(3)] for c in range(len(motion.descriptors))]
            changing = [i for i, r in enumerate(ranges) if max(r)>1e-5]
            mouth = {labels[i]:r for i,r in enumerate(ranges) if kind=='face' and any(s in labels[i] for s in ('Lip','Jaw','Bero')) and max(r)>1e-5}
            if kind=='face': assert mouth
            records.append(dict(character=ch, moveId=move_id, animation=move['animation'],kind=kind,
                propertyId=hex(prop_id),propertyFrame=prop['frame'],reference=reference,bank=bank,index=index,
                frames=len(motion.samples),channels=len(motion.descriptors),changingChannels=changing,
                animatedMouthChannels=mouth,archive=str(archive.relative_to(workspace)),
                archiveSha256=hashlib.sha256(data).hexdigest(),decodedFile=target.name,
                decodedSha256=hashlib.sha256(exported).hexdigest()))
    report=dict(tracks=records,playbackIntegrated=False,
        mappingEvidence=['UnityRemaster/AnimationResearch/Direct_TekkenDirect.py: left/right properties 0x842E/0x842F and face 0x84C2',
        'UnityRemaster/AnimationResearch/Direct_TekkenAnimHelper.py: 19 hand channels; 90 facial channels including jaw/lips/tongue'],
        limitations=['Native channels still require per-bone coordinate conversion and skin-weight validation.',
        'Kazuya export_kazuya.py collapses finger/facial weights onto body ancestors; restore original weights before playback.',
        'Kazuya secondary tracks must share the corrected entrance overlap timeline.'])
    (output/'discovery.json').write_text(json.dumps(report,indent=2)+'\n')
    for r in records:
        print(r['character'],r['kind'],'bank',r['bank'],'index',r['index'],'frames',r['frames'],'channels',r['channels'],'animated',len(r['changingChannels']),'mouth',len(r['animatedMouthChannels']))

if __name__ == '__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--workspace',type=Path,required=True)
    discover(parser.parse_args().workspace.resolve())
