"""Reproduce static evidence for Tekken's dynamic character-particle attachment.

Read-only input analysis. Addresses intentionally identify the installed 5.10
executable; assertions fail if the binary no longer matches these instructions.
"""
from pathlib import Path
import argparse
import hashlib
import json
import struct

from dispatch_native import Image


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    exe = args.workspace / 'TEKKEN.7/TekkenGame/Binaries/Win64/TekkenGame-Win64-Shipping.exe'
    im = Image(exe)
    def u64(va):
        return struct.unpack_from('<Q', im.data, im.offset(va))[0]
    def instructions(start, end):
        return im.instructions(start, end)
    def whole(va):
        return instructions(*im.function(va))
    def utf16(va):
        off = im.offset(va)
        end = off
        while im.data[end:end+2] != b'\0\0':
            end += 2
        return im.data[off:end].decode('utf-16le')
    assert u64(0x1427da200) == 0x1403cb9b0
    assert u64(0x1427da208) == 0x1403cbed0
    assert utf16(0x1427d99b0) == 'BonePlayerNumber'
    assert utf16(0x1427d99d8) == 'BoneIndex'
    ctor = whole(0x1403caf60)
    assert any(i['va'] == '0x1403caf70' and i['args'] == 'rax, [rip + 0x240f091]' for i in ctor)
    assert any(i['va'] == '0x1403cb04d' and i['args'] == 'byte ptr [rbx + 0x30], cl' for i in ctor)
    for va in (0x1403cb9b0, 0x1403cbed0):
        assert any(i['op'] == 'call' and i['args'] == '0x1403cb330' for i in whole(va))
    base_ctor = whole(0x141622690)
    assert any(i['va'] == '0x1416226e8' and i['args'] == 'dword ptr [rbx + 0x54], 2' for i in base_ctor)
    result = {
        'schema': 'tekken-native-character-bone-evidence/1',
        'executable': str(exe),
        'sha256': hashlib.sha256(im.data).hexdigest(),
        'imageBase': hex(im.base),
        'verified': {
            'class': 'PM_CharacterBone',
            'classRegistration': '0x14056c990',
            'constructorThunk': '0x14055c1e0',
            'constructor': '0x1403caf60',
            'vtable': '0x1427da008',
            'spawnVtableOffset': '0x1f8',
            'spawnFunction': '0x1403cb9b0',
            'updateVtableOffset': '0x200',
            'updateFunction': '0x1403cbed0',
            'dynamicSourceFunction': '0x1403cb330',
            'parametersRead': ['BonePlayerNumber', 'BoneIndex'],
            'parameterStorage': 'float, converted to int by cvttss2si',
            'boneNameLookup': '0x1402d7310; per-player ID-to-FName cache',
            'boneIndexOverridesSourceLocations': True,
            'sourceLocationWrite': 'SourceLocations[0] at module+0x40: FName from bone cache, zero XYZ offset; appends a record if empty',
            'sourceLocationResolution': 'both particle spawn and module update call the dynamic source function',
            'sourceType': 'custom constructor forces byte+0x30 to 0 (bone mode); socket mode 1 instead maps IDs 0..16 through a separate native switch',
            'existingParticleFollowing': {
                'gate': 'update function 0x1403cc027 tests byte[module+0x54] bit0; clear skips existing-particle location update',
                'baseConstructor': '0x1416226e8 sets only bit1 by OR 2; custom constructor does not set bit0',
                'jinNomalPackage': 'PM_CharacterBone_0 export54, offset40478, contains SourceLocations Neck with zero offset; bUpdatePositionEachFrame is not serialized',
                'result': 'new particles use the current bone position; existing particles are not dragged with that bone under these defaults',
            },
            'nativeSpawnTransform': {
                'property': '0x802e',
                'effectId': 0,
                'boneId': 'uint16(propertyValue)',
                'owner': 'self',
                'boneOwner': 'self',
                'pointLookup': '0x140732850(owner,outPoint,boneId)',
                'facingWordRead': '0x1407179af reads word[owner+0x1be]',
                'thetaRadians': 'int16(facingWord) * 2*pi/65536',
                'direction': ['sin(theta)', '0', 'cos(theta)'],
                'ueLocation': ['0.1*sourcePoint.z', '-0.1*sourcePoint.x', '0.1*sourcePoint.y+10'],
                'ueRotationDegrees': {
                    'pitch': 'atan2(direction.y,sqrt(direction.x^2+direction.z^2))*180/pi',
                    'yaw': 'atan2(direction.z,direction.x)*180/pi-90',
                    'roll': 0,
                },
                'locationConversionBeforeBoneWorldBranch': True,
                'orientation': 'initial world orientation follows owner facing, not animated bone orientation',
            },
        },
        'limitations': [
            'Static original-binary and package evidence establishes dispatch and attachment semantics; it is not an original-game visual comparison.',
            'The +10 Unreal Z adjustment is applied to the native point. Adding another 0.1m to an already-converted Unity rig bone can double an existing origin adjustment; compare coordinate conventions before conversion.',
            'Native custom mesh rotation/offset flags and exact particle shader rendering are separate from this attachment proof.',
            'Only property0x802e and effectID0 are asserted here. Other Jin folder systems retain unresolved move bindings.',
        ],
        'instructions': {
            'classRegistration': whole(0x14056c990),
            'constructorThunk': instructions(0x14055c1e0, 0x14055c1f4),
            'constructor': ctor,
            'baseConstructor': base_ctor,
            'dynamicSource': instructions(0x1403cb330, 0x1403cb5be),
            'spawnPrefix': instructions(0x1403cb9b0, 0x1403cb9f9),
            'updatePrefixAndFollowGate': instructions(0x1403cbed0, 0x1403cc03f),
            'sourcePropertyCase': instructions(0x1407179a4, 0x1407179f6),
            'sourceFacingVector': whole(0x140717350),
            'nativeBridge': whole(0x1402d87f0),
            'boneNameCacheLookup': whole(0x1402d7310),
        },
        'relatedEvidence': [
            'jin-timed-dispatch.json',
            'dispatch-character-manager.json',
            'bone-id-jin-vbn-map.json',
            'bone-id-native-particle-cache-init.json',
        ],
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({'out': str(args.out), 'sha256': result['sha256'], 'verified': True}))


if __name__ == '__main__':
    main()
