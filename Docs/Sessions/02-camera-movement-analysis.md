# Camera movement analysis

Dates: 2026-10-20 to 2026-10-21

Found where the Tekken 7 fight camera lives.

- The main fight camera is native C++ in the executable, not data.
- Decoded the throw_cameras record layout in the moveset files (9,193 records).
- Later turns of this session moved to an unrelated repo clone and a WSL problem.

Open: Camera behaviour was later rebuilt from the executable's tables by the fighting effect session.
