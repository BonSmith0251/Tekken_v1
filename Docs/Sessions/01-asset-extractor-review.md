# Asset extractor review

Dates: 2026-10-18 to 2026-10-23

Built and reviewed the t7extract toolkit that feeds the whole project.

- Verified the pak v3 format, the plaintext fast path and the AES-encrypted subset (4,307 entries), with the keys you supplied.
- Exporters for meshes and textures (glTF), audio (wav, and ogg for Vorbis), movies (needs a USM key from you) and the moves command (57 characters, 128,646 moves).
- Corrected an early figure: 15% of 47,899 move clips decode in the raw 0xC8 format, not 7%. The compressed 0x64 format is decoded in motion.py.
- 163 unit tests pass. Published an asset audit report as a separate artifact.

Open: Movies still need a USM key from you.
