"""Name Wwise IDs in a Tekken 7 sound bank by hashing candidate strings (read-only).

Wwise IDs of named objects (events, switch groups, switch/state values) are the 32-bit FNV-1 hash of the
lower-case name. Banks keep only the hashes, so names come from anywhere the game spells them: UE asset names
under Content/Sound, the executable (ASCII and UTF-16 strings), decrypted tkdata, the sound managers' imports.

Usage:
  python wwise_names.py --t7extract <t7extract> --bank <AkBankXXX.bnk> --strings <file or folder> [...]
  python wwise_names.py ... --event BGM_Container          # also print what that event plays
Prints the events, switch groups and switch values found, and how many stay unnamed.
"""
import argparse
import re
import sys
from pathlib import Path


def strings(path):
    data = Path(path).read_bytes()
    for m in re.finditer(rb"[A-Za-z0-9_\-]{3,64}", data):
        yield m.group().decode()
    for m in re.finditer(rb"(?:[A-Za-z0-9_\-]\x00){3,64}", data):
        yield m.group().decode("utf-16le")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--t7extract", required=True)
    ap.add_argument("--bank", required=True)
    ap.add_argument("--strings", nargs="+", default=[], help="files or folders to take candidate names from")
    ap.add_argument("--names", nargs="*", default=[], help="extra candidate names")
    ap.add_argument("--event", help="show the tree an event plays")
    args = ap.parse_args()
    sys.path.insert(0, args.t7extract)
    from t7extract.wwise_hirc import Bank, EVENT, SOUND, SWITCH, fnv1

    bank = Bank(Path(args.bank).read_bytes())
    candidates = set(args.names) | {"Normal", "Final", "NONE", "None"}
    for s in args.strings:
        p = Path(s)
        for f in (p.rglob("*") if p.is_dir() else [p]):
            if f.is_file() and f.stat().st_size < 80_000_000:
                candidates.update(strings(f))
                candidates.update(f.stem for f in [f])
    names = {fnv1(c): c for c in candidates}

    events = [n for n in bank.nodes.values() if n.type == EVENT]
    switches = [n for n in bank.nodes.values() if n.type == SWITCH and n.switches]
    groups = {n.group_id for n in switches}
    values = {k for n in switches for k in n.switches}
    named_events = sorted(names[e.id] for e in events if e.id in names)
    print(f"events {len(named_events)}/{len(events)} named: {named_events}")
    print("switch groups:", sorted(names.get(g, str(g)) for g in groups))
    print(f"switch values {sum(v in names for v in values)}/{len(values)} named:",
          sorted(names[v] for v in values if v in names)[:400])

    if args.event:
        def show(oid, depth=0):
            n = bank.nodes.get(oid)
            pad = "  " * depth
            if n is None:
                print(pad + f"{oid} (another bank)"); return
            if n.type == SOUND:
                print(pad + f"sound media {n.source_id} ({'streamed' if n.stream_type else 'in bank'})"); return
            if n.type == SWITCH:
                print(pad + f"switch on {names.get(n.group_id, n.group_id)}, default {names.get(n.default_switch, n.default_switch)}")
                for k, ids in list(n.switches.items())[:40]:
                    print(pad + f"  [{names.get(k, k)}]")
                    for i in ids[:2]:
                        show(i, depth + 2)
                return
            print(pad + f"node type {n.type}")
            for c in n.children[:12]:
                show(c, depth + 1)
        for target in bank.event_targets(fnv1(args.event)):
            show(target, 1)


if __name__ == "__main__":
    main()
