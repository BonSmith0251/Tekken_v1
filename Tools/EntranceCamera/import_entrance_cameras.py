"""Export original entrance cameras from MOTA bank 4, using move property 33845.

Run with --workspace pointing to the folder containing decrypted/tkdata.
Samples remain in native units and radians; Unity performs the rig conversion.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def export(workspace, project):
    output = project / 'Assets/Remaster/Resources/EntranceCameras'
    output.mkdir(parents=True, exist_ok=True)
    records = []
    for character, folder, move_id, resource in [
        ('jin', 'jin_motion', 2218, 'Jin_s00'),
        ('kaz', 'kazuya_motion', 2247, 'Kazuya_s06'),
    ]:
        moves = json.loads((workspace / f'decrypted/{folder}/{character}/moveset.json').read_text())['moves']
        move = next(m for m in moves if m['id'] == move_id)
        reference = next(p['value'] for p in move['properties'] if p['id'] == 33845)
        assert reference & 0xC000 == 0x4000
        index = reference & 0x3FFF
        archive = workspace / f'decrypted/tkdata/mothead/mask/{character}rotsm.arc'
        data = archive.read_bytes()
        offset, size = struct.unpack_from('<II', data, 4 + 4 * 8)
        assert data[offset:offset + 4] == b'MOTA'
        count = struct.unpack_from('>I', data, offset + 12)[0]
        assert index < count
        start = offset + struct.unpack_from('>I', data, offset + 20 + index * 4)[0]
        kind, channels, frames = struct.unpack_from('>HHI', data, start)
        assert (kind, channels) == (0xC8, 4)
        assert struct.unpack_from('>4I', data, start + 8) == (11, 7, 15, 21)
        assert frames == move['anim']['frames']
        end = start + 24 + frames * 48
        assert end <= offset + size
        values = struct.unpack_from(f'>{frames * 12}f', data, start + 24)
        target = output / (resource + '.bytes')
        target.write_bytes(struct.pack('<4sIIII', b'T7C1', frames, 60, move_id, index)
                          + struct.pack(f'<{len(values)}f', *values))
        records.append(dict(resource=resource, moveId=move_id, animation=move['animation'],
                            propertyId=33845, propertyValue=reference, bank=4, track=index,
                            frames=frames, fps=60, source=str(archive.relative_to(workspace)),
                            archiveSha256=hashlib.sha256(data).hexdigest(),
                            cameraSha256=hashlib.sha256(data[start:end]).hexdigest(),
                            conversion='Native samples retained; Unity maps millimeters and XYZ Euler radians to the retargeted rig. FOV interpreted as horizontal radians.',
                            originalGameVisualComparison=False))
    (Path(__file__).parent / 'source-manifest.json').write_text(json.dumps(records, indent=2) + '\n')
    print(json.dumps(records, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--workspace', type=Path, required=True)
    args = parser.parse_args()
    export(args.workspace.resolve(), Path(__file__).resolve().parents[2])
