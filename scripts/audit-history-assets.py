"""Audits every blob of the repository's history: size, binary or text, and whether its content equals a file of the game.

Usage: python audit_history.py <repo> <out-dir> <hash-list.tsv>...
A hash list has lines "<sha256>\t<length>\t<path>" (the installation index) or is a JSON content manifest with sha256 fields.
"""
import hashlib, json, os, re, subprocess, sys

repo, out = sys.argv[1], sys.argv[2]
known = {}
for path in sys.argv[3:]:
    text = open(path, 'r', encoding='utf-8-sig', errors='replace').read()
    if path.lower().endswith('.json'):
        for m in re.finditer(r'"([0-9A-Fa-f]{64})"', text):
            known.setdefault(m.group(1).upper(), os.path.basename(path))
    else:
        for line in text.splitlines():
            parts = line.split('\t')
            if len(parts) >= 3 and len(parts[0]) == 64:
                known.setdefault(parts[0].upper(), parts[2])

objects = subprocess.run(['git', '-C', repo, 'rev-list', '--all', '--objects'], capture_output=True, text=True, encoding='utf-8', errors='replace').stdout.splitlines()
paths = {}
for line in objects:
    sha, _, name = line.partition(' ')
    if name:
        paths.setdefault(sha, name)

proc = subprocess.Popen(['git', '-C', repo, 'cat-file', '--batch'], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
rows, matches = [], []
for sha, name in paths.items():
    proc.stdin.write((sha + '\n').encode()); proc.stdin.flush()
    header = proc.stdout.readline().split()
    if len(header) < 3 or header[1] != b'blob':
        if len(header) >= 3:
            proc.stdout.read(int(header[2]) + 1)
        continue
    size = int(header[2]); data = proc.stdout.read(size); proc.stdout.read(1)
    digest = hashlib.sha256(data).hexdigest().upper()
    binary = b'\0' in data[:8000]
    ext = os.path.splitext(name)[1].lower()
    rows.append((size, 'binary' if binary else 'text', ext, name, digest))
    if size > 0 and digest in known:
        matches.append((name, size, known[digest]))
proc.stdin.close()

os.makedirs(out, exist_ok=True)
with open(os.path.join(out, 'history-blobs.tsv'), 'w', encoding='utf-8', newline='\n') as f:
    for row in sorted(rows, key=lambda r: -r[0]):
        f.write('\t'.join(str(x) for x in row) + '\n')
print(f'blobs in history: {len(rows)}; known game hashes: {len(known)}')
print(f'blobs equal to a game file: {len(matches)}')
for name, size, game in sorted(matches)[:60]:
    print(f'  MATCH {name} ({size} bytes) == {game}')
binaries = [r for r in rows if r[1] == 'binary']
print(f'binary blobs: {len(binaries)}')
by_ext = {}
for r in rows:
    key = (r[2] or '(none)', r[1])
    c, s = by_ext.get(key, (0, 0)); by_ext[key] = (c + 1, s + r[0])
print('by extension and kind (count, bytes):')
for key, (c, s) in sorted(by_ext.items(), key=lambda kv: -kv[1][1])[:40]:
    print(f'  {key[0]:14} {key[1]:6} {c:6} {s:12}')
print('largest 25 blobs:')
for r in sorted(rows, key=lambda r: -r[0])[:25]:
    print(f'  {r[0]:10} {r[1]:6} {r[3]}')
print('binary blobs (all):')
for r in sorted(binaries, key=lambda r: r[3]):
    print(f'  {r[0]:10} {r[3]}')
