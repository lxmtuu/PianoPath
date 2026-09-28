Keyflow – handoff files (generated 2026-09-29)

keyflow-update.patch  : `git diff` from origin/main (798cdaa) to the current workspace tree.
                        Apply on a fresh clone of main:  git apply --check keyflow-update.patch && git apply keyflow-update.patch
keyflow-source.zip    : the same final tree as plain files (PianoPath/…), WITHOUT Assets/ConcertGrand.sf2
                        so it never overwrites the real SoundFont. Extract over a clone, then `git add -A`.

Do not commit this folder; delete it before `git add -A` (or add `handoff/` to .git/info/exclude).
