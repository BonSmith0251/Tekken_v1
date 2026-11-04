# git lfs setting

Dates: 2026-10-26

Moved the repository to Git LFS.

- History rewritten so images, audio and .bytes files live in LFS (2.68 GiB to 0.45 GiB plus about 2.6 GB of LFS at the time).
- LFS storage lives on C: because writes on D: failed. Pushes need the proxy unset and non-sparse packing.

Open: Done.
