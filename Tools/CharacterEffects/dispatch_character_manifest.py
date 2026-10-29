"""Reproduce a fighter's 0x802E native dispatch evidence: timed events and VBN bone IDs.

Generalises dispatch_jin_manifest.py. The native case (effect ID 0, owner self, bone from the
property value) is character independent; this checks it again for the installed executable, reads
the fighter's own moveset and VBN from the original tkdata.bin, and takes the effect-ID-0 binding
from that fighter's extracted manager evidence (inspect_character_effects.py output).
"""
from pathlib import Path
import argparse,collections,hashlib,json,struct,sys
from dispatch_native import Image

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--workspace',type=Path,required=True)
    ap.add_argument('--character',required=True,help='Original character code, e.g. ltn')
    ap.add_argument('--manager-evidence',type=Path,required=True,help='Folder holding character-effects.json')
    ap.add_argument('--out',type=Path,required=True,help='Evidence folder to write')
    args=ap.parse_args();w=args.workspace.resolve();c=args.character;sys.path.insert(0,str(w/'t7extract'))
    from t7extract.moveset import parse_moveset,DATA_BASE
    from t7extract.tkdata import TkDataFile
    exe=w/'TEKKEN.7/TekkenGame/Binaries/Win64/TekkenGame-Win64-Shipping.exe'
    image=Image(exe)
    tableVA=image.base+0x71FA10
    target=struct.unpack_from('<I',image.data,image.offset(tableVA))[0]+image.base
    assert target==0x1407179A4,'Native switch layout changed; re-investigate this installation'

    container=w/'TEKKEN.7/TekkenGame/Content/Binary/tkdata.bin'
    with TkDataFile(container) as tk:
        entries={e.path.replace('\\','/').lower():e for e in tk.entries}
        mot=entries[f'mothead/bin/{c}mot.bin'];vbn=entries[f'mothead/nua/{c}.vbn']
        data=mot.read();vbnData=vbn.read()
    for name,raw in ((f'mothead/bin/{c}mot.bin',data),(f'mothead/nua/{c}.vbn',vbnData)):
        copy=w/'decrypted/tkdata'/name
        assert not copy.exists() or copy.read_bytes()==raw,f'Decrypted copy differs from tkdata.bin: {name}'

    # Effect ID 0 binding from the fighter's own manager defaults.
    report=json.loads((args.manager_evidence/'character-effects.json').read_text(encoding='utf-8'))
    manager=report['managers'][0]
    zero=[e for e in manager['properties']['Effects'] if e['EffectID']==0]
    assert len(zero)==1 and zero[0]['ParticleSystem'],'Manager has no single effect ID 0 particle binding'
    binding=zero[0]['ParticleSystem']['object']

    # VBN bone table: count at +8, records of 0x4C bytes from +0x1C, name first (native 0x1406ccba0).
    assert vbnData[:4]==b' NBV','Not a VBN bone table'
    count=struct.unpack_from('<I',vbnData,8)[0]
    bones=[]
    for i in range(count):
        off=0x1C+i*0x4C;name=vbnData[off:off+0x4C].split(b'\0',1)[0].decode('ascii')
        bones.append({'boneId':i,'name':name,'vbnOffset':off})
    names={b['boneId']:b['name'] for b in bones}
    assert names[0]=='BASE' and names[7]=='R_Arm' and names[9]=='R_Hand','Unexpected VBN layout'

    ms=parse_moveset(data);sha=hashlib.sha256(data).hexdigest()
    graph=w/f'UnityRemaster/TekkenVersusRemaster/Assets/_Project/Resources/CommandGraph/{c}.json'
    g=json.loads(graph.read_text(encoding='utf-8'));assert sha==g['sha256'],'Command graph was exported from another moveset'
    rows=[]
    for gm in g['moves']:
        m=ms.move(gm['id'],cancels=False)
        raw=ms.record('moves',m['id']);first=struct.unpack_from('<Q',raw,0x80)[0]
        source=m.get('properties',[]);flat=[v for p in source for v in [p['frame'],p['id'],p['value']]]
        assert flat==gm['properties'],f'Graph differs from original move {m["id"]}'
        for ordinal,p in enumerate(source):
            if p['id']!=0x802e:continue
            idx=first+ordinal
            off=DATA_BASE+ms.blocks['extra_props'][0]+idx*12
            assert struct.unpack_from('<III',data,off)==(p['frame'],p['id'],p['value'])
            assert (p['value']&0xffff) in names,f'Bone {p["value"]&0xffff} missing from {c}.vbn'
            rows.append({'moveId':m['id'],'moveName':m['name'],'animation':m['animation'],
                         'rawFrame':p['frame'],'propertyId':p['id'],'propertyValue':p['value'],
                         'propertyRecordIndex':idx,'propertyPackageOffset':off,
                         'effectId':0,'boneId':p['value']&0xffff,'boneName':names[p['value']&0xffff],
                         'owner':'self','boneOwner':'self',
                         'triggerKind':'original timed property visual pass','binding':binding})
    exeSha=hashlib.sha256(image.data).hexdigest()
    events={
      'schema':'tekken-character-effect-dispatch/1','characterCode':c,
      'executable':{'path':str(exe),'sha256':exeSha,'imageBase':hex(image.base)},
      'moveset':{'path':f'{container.name}:mothead/bin/{c}mot.bin','sha256':sha,'commandGraphMatchesOriginalAllMoves':True},
      'manager':{'package':manager['source'],'sha256':manager['sourceSha256'],'effectId0':binding},
      'propertyId':0x802e,'effectId':0,'events':rows,
      'nativeEvidence':{
        'propertyInterpreterVA':'0x140717740','visualPassSwitchTableVA':hex(tableVA),
        'caseTargetVA':hex(target),'caseInstructions':image.instructions(target,0x1407179F6),
        'pointLookupVA':'0x140732850','boneSpawnHelperVA':'0x140717350',
        'worldSelection':'native bridge selects SpawnWorld when boneId == -1, SpawnBone otherwise',
        'managerBehavior':'FindEffectsNative(effectId), SpawnEmitterAtLocation(position, rotation, true) on the acting fighter\'s own manager.',
        'sameCaseAs':'dispatch_jin_manifest.py; the case reads no character code'},
      'counts':{'timedEvents':len(rows),'moves':len({x['moveId'] for x in rows}),
                'boneIds':{str(k):v for k,v in sorted(collections.Counter(x['boneId'] for x in rows).items())}},
      'limitations':[
        'Static package/executable evidence, not a recorded original-game trace.',
        'Native bone IDs resolve through the fighter\'s VBN table; they are not animation channels or Unity transform indices.',
        'Other properties, collision-driven character effects, start/end event tables and item overrides are outside this manifest.']}
    boneMap={
      'schema':'tekken-native-bone-map/1','characterCode':c,'container':str(container.relative_to(w)),
      'entry':f'mothead/nua/{c}.vbn','entrySha256':hashlib.sha256(vbnData).hexdigest(),'entryBytes':len(vbnData),
      'nativeExecutableSha256':exeSha,'vbnNameGetter':'0x1406ccba0',
      'formatProof':'0x1406ccba0 reads bone count at header+8; returns header+0x1c+boneId*0x4c (see bone-id-jin-vbn-map.json)',
      'bones':bones}
    args.out.mkdir(parents=True,exist_ok=True)
    (args.out/f'{c}-timed-dispatch.json').write_text(json.dumps(events,indent=2)+'\n',encoding='utf-8')
    (args.out/f'bone-id-{c}-vbn-map.json').write_text(json.dumps(boneMap,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'out':str(args.out),'binding':binding,'bones':count,**events['counts']}))

if __name__=='__main__':main()
