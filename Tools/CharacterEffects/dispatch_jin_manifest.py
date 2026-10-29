"""Reproduce Jin's 0x802E native dispatch evidence and source event inventory."""
from pathlib import Path
import argparse,collections,hashlib,json,struct,sys
from dispatch_native import Image

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--workspace',type=Path,required=True);ap.add_argument('--out',type=Path,required=True)
    args=ap.parse_args();w=args.workspace.resolve();sys.path.insert(0,str(w/'t7extract'))
    from t7extract.moveset import parse_moveset,DATA_BASE,NONE
    exe=w/'TEKKEN.7/TekkenGame/Binaries/Win64/TekkenGame-Win64-Shipping.exe'
    image=Image(exe)
    moveset=w/'decrypted/tkdata/mothead/bin/jinmot.bin';data=moveset.read_bytes();ms=parse_moveset(data)
    graph=w/'UnityRemaster/TekkenVersusRemaster/Assets/_Project/Resources/CommandGraph/jin.json';g=json.loads(graph.read_text())
    sha=hashlib.sha256(data).hexdigest();assert sha==g['sha256']
    tableVA=image.base+0x71FA10
    target=struct.unpack_from('<I',image.data,image.offset(tableVA))[0]+image.base
    assert target==0x1407179A4,'Native switch layout changed; re-investigate this installation'
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
            rows.append({'moveId':m['id'],'moveName':m['name'],'animation':m['animation'],
                         'rawFrame':p['frame'],'propertyId':p['id'],'propertyValue':p['value'],
                         'propertyRecordIndex':idx,'propertyPackageOffset':off,
                         'effectId':0,'boneId':p['value']&0xffff,'owner':'self','boneOwner':'self',
                         'triggerKind':'original timed property visual pass',
                         'binding':'PS_EF_CHR_JIN_NOMAL'})
    evidence={
      'schema':'tekken-jin-effect-dispatch/1','characterCode':'jin',
      'executable':{'path':str(exe),'sha256':hashlib.sha256(image.data).hexdigest(),'imageBase':hex(image.base)},
      'moveset':{'path':str(moveset),'sha256':sha,'commandGraphMatchesOriginalAllMoves':True},
      'propertyId':0x802e,'effectId':0,'events':rows,
      'nativeEvidence':{
        'propertyInterpreterVA':'0x140717740','visualPassSwitchTableVA':hex(tableVA),
        'caseTargetVA':hex(target),'caseInstructions':image.instructions(target,0x1407179F6),
        'pointLookupVA':'0x140732850','boneSpawnHelperVA':'0x140717350',
        'characterEventHelperVA':'0x140717600','event9BridgeVA':'0x14073F530',
        'interfaceVtableVA':'0x1427AE530','interfaceSlot':0x50,'nativeBridgeVA':'0x1402D87F0',
        'spawnBoneWrapperVA':'0x1405743F0','spawnWorldWrapperVA':'0x140574580',
        'worldSelection':'native bridge selects SpawnWorld when boneId == -1, SpawnBone otherwise',
        'managerBehavior':'FindEffectsNative(effectId), SpawnEmitterAtLocation(position, rotation, true); AddParticleSystem(owner,effectId,instance); bone path sets BoneSocketActor, PlayerNumber, BonePlayerNumber, BoneIndex, HistoryNumber.',
        'onHitBehavior':'Inherited OnHit copies parameters and enters ubergraph VM offset4798, which pops to return. Jin subclass has no function overrides.'},
      'counts':{'timedEvents':len(rows),'moves':len({x['moveId'] for x in rows}),'boneIds':dict(collections.Counter(x['boneId'] for x in rows))},
      'limitations':[
        'This manifest describes static package/executable evidence, not a recorded original-game trace.',
        'Native bone IDs must be resolved through original bone tables; they are not animation channel or Unity transform indices.',
        'Source point lookup and facing conversion are native; Unity retargeted bone transforms are an adaptation that needs visual validation.',
        'Other properties, collision-driven character effects, start/end event tables, flags and override item variants are outside this initial 0x802E manifest.',
        'An effect registry binding does not establish how FindEffectsNative combines customization overrides.']}
    args.out.parent.mkdir(parents=True,exist_ok=True);args.out.write_text(json.dumps(evidence,indent=2)+'\n')
    print(json.dumps({'out':str(args.out),**evidence['counts'],'allFramesUnflagged':all(x['rawFrame']<0x2000 for x in rows)}))

if __name__=='__main__':main()
