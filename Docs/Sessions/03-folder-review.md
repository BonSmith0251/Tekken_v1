# Folder review

Dates: 2026-10-22

The first review of the game folder and the UnityRemaster project. Later sessions were forked from it.

- Reviewed the folder layout and the project's progress against the Codex skill's checklist.
- Found why Jin's eyes looked wrong (eye_shadow layers drawn opaque) and fixed it with a shared transparent blended layer, plus soft and contact shadows in staging_test.
- The museum chrome-eye problem was traced to a specular-colour material mode; the material is now in standard mode and you confirmed the eyes look right.

Open: Done.
