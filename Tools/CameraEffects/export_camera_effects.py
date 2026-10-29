"""Extract original camera events and C8 look-at tracks directly from tkdata.bin.
No Unity graph is used as the source. Native shake dispatch is verified against
the inspected executable; amplitudes/waveforms remain explicitly unrecovered.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

PROPERTIES = {0x8435, 0x8436, 0x8437, 0x8438, 0x8439, 0x843A, 0x8445, 0x8448, 0x8449}
SHAKES = {0x8001: 3, 0x8002: 4, 0x8003: 5, 0x8004: 6,
          0x8006: 3, 0x8007: 4, 0x8008: 5, 0x8009: 6}
EXE_HASH = 'f46f9ff526fe5d00a0d1ee52126f61628cdc13c402264e2764e4010d3b9c1892'


def digest(data): return hashlib.sha256(data).hexdigest()


def decode_track(raw, index):
    offset, size = struct.unpack_from('<II', raw, 4 + 4 * 8)
    if offset + size > len(raw) or raw[offset:offset + 4] != b'MOTA':
        raise ValueError('Invalid camera bank 4')
    count = struct.unpack_from('>I', raw, offset + 12)[0]
    if not 0 <= index < count or 20 + count * 4 > size: raise ValueError('Camera index/table out of bounds')
    pointers = struct.unpack_from(f'>{count}I', raw, offset + 20)
    relative = pointers[index]
    end = offset + min([p for p in pointers if p > relative] + [size])
    start = offset + relative
    if start < offset + 20 + count * 4 or start + 8 > end: raise ValueError('Camera header out of bounds')
    kind, channels, frames = struct.unpack_from('>HHI', raw, start)
    if kind != 0xC8 or not 0 < channels <= 32 or not 0 < frames <= 100000: raise ValueError('Unsupported camera header')
    data = start + 8 + channels * 4
    finish = data + frames * channels * 12
    if finish > end: raise ValueError('Camera samples cross track bound')
    descriptors = struct.unpack_from(f'>{channels}I', raw, start + 8)
    values = struct.unpack_from(f'>{frames * channels * 3}f', raw, data)
    if not all(math.isfinite(v) for v in values): raise ValueError('Nonfinite camera sample')
    output = struct.pack('<4sII', b'T7CF', frames, channels) + struct.pack(f'<{channels}I', *descriptors) + struct.pack(f'<{len(values)}f', *values)
    return output, {'track': index, 'frames': frames, 'descriptors': list(descriptors), 'archiveOffset': start,
                    'sourceTrackSha256': digest(raw[start:finish]), 'exportSha256': digest(output)}


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--workspace', required=True, type=Path)
    ap.add_argument('--out', required=True, type=Path, help='New local evidence/export directory')
    ap.add_argument('--characters', nargs='+', default=['jin', 'kaz', 'xia', 'asa', 'ltn'])
    args = ap.parse_args(); w = args.workspace.resolve(); out = args.out.resolve()
    if out.exists(): ap.error('Choose a new output directory')
    project = w / 'UnityRemaster/TekkenVersusRemaster'
    config = json.loads((project / 'Tools/source-paths.json').read_text())
    sys.path.insert(0, config['asset_workshop'])
    sys.path.insert(0, str(project / 'Tools/CharacterEffects'))
    from t7extract.tkdata import TkDataFile
    from t7extract.moveset import parse_moveset, DATA_BASE
    from dispatch_native import Image
    game = Path(config['game']); exe = game / 'TekkenGame/Binaries/Win64/TekkenGame-Win64-Shipping.exe'
    image = Image(exe); exe_hash = digest(image.data)
    if exe_hash != EXE_HASH: raise ValueError('Executable changed; re-establish native camera dispatch before exporting')
    native = []
    for prop, preset in SHAKES.items():
        target = image.base + struct.unpack_from('<I', image.data, image.offset(image.base + 0x71fc98) + (prop - 0x8001) * 4)[0]
        rows = image.instructions(target, target + 27)
        if not any(r['op'] == 'mov' and r['args'] == f'edx, {preset}' for r in rows): raise ValueError('Preset mapping changed')
        if not any(r['op'] == 'call' and r['args'] == '0x1406ff050' for r in rows): raise ValueError('Dispatch target changed')
        native.append({'propertyId': prop, 'preset': preset, 'caseVA': hex(target), 'instructions': rows})
    out.mkdir(parents=True); (out / 'FightCameras').mkdir(); (out / 'original').mkdir()
    manifest = {'schema': 'tekken-camera-effects/1', 'executableSha256': exe_hash, 'characters': [], 'tracks': [],
                'events': [], 'shakeDispatch': native, 'originalVisualComparison': False,
                'limitations': ['Camera track samples and event records are original; Unity axes, lens and attachment need original visual comparison.',
                                'Shake preset IDs 3..6 are recovered. Waveform, amplitude and duration are not recovered; demo comparison uses existing remaster values.',
                                '0x8439/0x843A side selection and 0x84CC zoom behavior remain existing remaster interpretations, not proven by the shake dispatch case.',
                                'Only five-channel 11/11/15/3/21 look-at tracks exported here; other layouts retained as excluded metadata.']}
    with TkDataFile(game / 'TekkenGame/Content/Binary/tkdata.bin') as tk:
        entries = {e.path.replace('\\', '/').lower(): e for e in tk.entries}
        for code in args.characters:
            motion_name = f'mothead/bin/{code}mot.bin'; camera_name = f'mothead/mask/{code}rotsm.arc'
            data = entries[motion_name].read(); raw = entries[camera_name].read()
            (out / 'original' / f'{code}mot.bin').write_bytes(data)
            (out / 'original' / f'{code}rotsm.arc').write_bytes(raw)
            ms = parse_moveset(data)
            # The graph is checked only as a consumer of the original moveset.
            graph = json.loads((project / f'Assets/_Project/Resources/CommandGraph/{code}.json').read_text())
            if graph['sha256'] != digest(data): raise ValueError('Unity graph source mismatch: ' + code)
            manifest['characters'].append({'code': code, 'movesetEntry': motion_name, 'movesetSha256': digest(data),
                                           'cameraEntry': camera_name, 'cameraArchiveSha256': digest(raw)})
            tracks = {}
            for index in range(ms.blocks['moves'][1]):
                move = ms.move(index, cancels=False)
                first = struct.unpack_from('<Q', ms.record('moves', index), 0x80)[0]
                for ordinal, p in enumerate(move.get('properties', [])):
                    if p['id'] not in PROPERTIES | SHAKES.keys() | {0x84CC}: continue
                    offset = DATA_BASE + ms.blocks['extra_props'][0] + (first + ordinal) * 12
                    if struct.unpack_from('<III', data, offset) != (p['frame'], p['id'], p['value']): raise ValueError('Event bytes mismatch')
                    row = {'code': code, 'moveId': index, 'moveName': move['name'], 'rawFrame': p['frame'],
                           'propertyId': p['id'], 'value': p['value'], 'sourceOffset': offset}
                    manifest['events'].append(row)
                    if p['id'] in PROPERTIES and p['value'] & 0xC000 == 0x4000:
                        track = p['value'] & 0x3FFF
                        if track not in tracks:
                            blob, info = decode_track(raw, track)
                            info.update(code=code, resource=f'{code}_{track}', moves=[])
                            info['supported'] = info['descriptors'] == [11, 11, 15, 3, 21]
                            if info['supported']: (out / 'FightCameras' / (info['resource'] + '.bytes')).write_bytes(blob)
                            tracks[track] = info
                        tracks[track]['moves'].append(row)
            manifest['tracks'].extend(tracks.values())
    (out / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print(json.dumps({'events': len(manifest['events']), 'lookAtTracks': sum(t['supported'] for t in manifest['tracks']), 'out': str(out)}))


if __name__ == '__main__': main()
