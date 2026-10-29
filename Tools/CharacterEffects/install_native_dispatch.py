"""Install a fighter's recovered 0x802E dispatch after checking graph, executable and bone evidence."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--evidence', type=Path, required=True)
parser.add_argument('--project', type=Path, default=Path(__file__).resolve().parents[2])
parser.add_argument('--character', default='jin', help='Original character code (jin, ltn)')
args = parser.parse_args()
def read(name):
    return json.loads((args.evidence / name).read_text(encoding='utf-8'))
code = args.character
events = read(f'{code}-timed-dispatch.json')
bones = read(f'bone-id-{code}-vbn-map.json')
attachment = read('dispatch-bone-attachment-proof.json')
exe_hash = events['executable']['sha256']
assert bones['nativeExecutableSha256'] == attachment['sha256'] == exe_hash
assert events.get('characterCode', 'jin') == code
assert events['propertyId'] == 0x802E and events['effectId'] == 0
assert events['moveset']['commandGraphMatchesOriginalAllMoves']
assert attachment['verified']['boneIndexOverridesSourceLocations']
assert attachment['verified']['nativeSpawnTransform']['locationConversionBeforeBoneWorldBranch']
graph = json.loads((args.project / f'Assets/_Project/Resources/CommandGraph/{code}.json').read_text(encoding='utf-8'))
assert graph['sha256'] == events['moveset']['sha256']
actual = []
for move in graph['moves']:
    props = move.get('properties') or []
    for i in range(0, len(props) - 2, 3):
        if props[i + 1] == 0x802E:
            actual.append((move['id'], props[i], props[i + 2]))
expected = [(e['moveId'], e['rawFrame'], e['propertyValue']) for e in events['events']]
assert actual == expected, 'Current graph differs from the inspected original timed events'
names = {b['boneId']: b['name'] for b in bones['bones']}
assert all(e['boneId'] in names and e['rawFrame'] < 0x4000 for e in events['events'])
assert names[7] == 'R_Arm' and names[9] == 'R_Hand'
manifest = {
    'schema': 'tekken-native-character-effects/1', 'characterCode': code,
    'propertyId': 0x802E, 'effectId': 0, 'boneIndexOverridesSourceLocations': True,
    'rotation': 'actor', 'worldSpawnOffset': [0, 0.1, 0],
    'existingParticlesFollowBone': False,
    'bones': [{'id': b['boneId'], 'name': b['name']} for b in bones['bones']],
    'events': events['events'], 'counts': events['counts'],
    'sourceExecutableSha256': exe_hash, 'sourceMovesetSha256': graph['sha256'],
    'sourceBoneTableSha256': bones['entrySha256'],
    'nativeEvidence': events['nativeEvidence'], 'attachmentEvidence': attachment['verified'],
    'evidenceFiles': [{'path': n, 'sha256': hashlib.sha256((args.evidence / n).read_bytes()).hexdigest()}
                      for n in (f'{code}-timed-dispatch.json', f'bone-id-{code}-vbn-map.json', 'dispatch-bone-attachment-proof.json')],
    'limitations': events['limitations'] + [
        'Unity uses retargeted rig world points and displayed actor facing; comparison with an original-game capture remains required.',
        'The native +10 Unreal-unit Z spawn offset is converted to +0.1 metre world up for mesh emitter origins; PM_CharacterBone uses the zero-offset resolved rig bone.',
        'Versus queues acted-frame events until its actors are posed; multiple simulation ticks retain all events but use the next displayed pose.',
        'Random atlas selection, custom sockets, omitted material operations and unsupported emitter variants remain listed in conversion-report.json.'
    ]
}
folder = args.project / f'Assets/_Project/Resources/CharacterEffects/{code}'
(folder / 'native-dispatch.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
registry_path = folder / 'registry.json'
registry = json.loads(registry_path.read_text(encoding='utf-8'))
assert registry['characterCode'] == code
bound = [b['system'] for b in registry['bindings'] if b['effectId'] == 0]
assert len(bound) == 1 and all(e['binding'] == bound[0] for e in events['events'])
for binding in registry['bindings']:
    if binding['effectId'] == 0:
        binding['bindingStatus'] = 'original manager binding; native timed property 0x802E verified in native-dispatch.json'
for candidate in registry['candidates']:
    if candidate['system'] == bound[0]:
        candidate['previewOnly'] = False
registry['nativeTriggerMappings'] = [{'propertyId': 0x802E, 'effectId': 0, 'manifest': 'native-dispatch.json',
                                      'timedEvents': len(expected), 'moves': events['counts']['moves']}]
registry_path.write_text(json.dumps(registry, indent=2) + '\n', encoding='utf-8')
print(f'Installed original 0x802E dispatch: {len(expected)} events, {events["counts"]["moves"]} moves, {len(names)} bone IDs.')
