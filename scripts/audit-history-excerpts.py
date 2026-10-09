"""Looks for verbatim lines of the game's text files (INI, WND, STR, TXT, scripts as text) inside the repository's history blobs.

Usage: python audit_excerpts.py <repo> <content-dir> <out-dir> [min-length]
A "distinctive" game line: at least min-length characters after whitespace normalisation, with at least 6 letters.
"""
import os, re, subprocess, sys

repo, content, out = sys.argv[1], sys.argv[2], sys.argv[3]
minimum = int(sys.argv[4]) if len(sys.argv) > 4 else 60
norm = lambda s: re.sub(r'\s+', ' ', s.strip())
game = {}
count_files = 0
for root, _, files in os.walk(content):
    for name in files:
        if os.path.splitext(name)[1].lower() not in ('.ini', '.wnd', '.str', '.txt', '.csv', '.lst', '.cfg', '.vdf', '.lcf'):
            continue
        count_files += 1
        try:
            text = open(os.path.join(root, name), 'r', encoding='latin-1').read()
        except OSError:
            continue
        rel = os.path.relpath(os.path.join(root, name), content)
        for line in text.splitlines():
            n = norm(line)
            if len(n) >= minimum and sum(c.isalpha() for c in n) >= 6:
                game.setdefault(n, rel)
print(f'game text files: {count_files}; distinctive lines: {len(game)}')

objects = subprocess.run(['git', '-C', repo, 'rev-list', '--all', '--objects'], capture_output=True, text=True, encoding='utf-8', errors='replace').stdout.splitlines()
paths = {}
for line in objects:
    sha, _, name = line.partition(' ')
    if name:
        paths.setdefault(sha, name)
proc = subprocess.Popen(['git', '-C', repo, 'cat-file', '--batch'], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
hits = {}
examples = {}
for sha, name in paths.items():
    proc.stdin.write((sha + '\n').encode()); proc.stdin.flush()
    header = proc.stdout.readline().split()
    if len(header) < 3:
        continue
    size = int(header[2]); data = proc.stdout.read(size); proc.stdout.read(1)
    if header[1] != b'blob':
        continue
    found = 0
    for line in data.decode('utf-8', errors='replace').splitlines():
        n = norm(line)
        if len(n) >= minimum and n in game:
            found += 1
            examples.setdefault(name, []).append((n, game[n]))
    if found:
        hits[name] = max(hits.get(name, 0), found)
proc.stdin.close()
os.makedirs(out, exist_ok=True)
print(f'repository paths with at least one verbatim game line (max over history): {len(hits)}')
with open(os.path.join(out, 'history-excerpts.txt'), 'w', encoding='utf-8', newline='\n') as f:
    for name, n in sorted(hits.items(), key=lambda kv: -kv[1]):
        f.write(f'{n}\t{name}\n')
        for line, source in examples[name][:8]:
            f.write(f'\t{source}: {line[:200]}\n')
for name, n in sorted(hits.items(), key=lambda kv: -kv[1])[:40]:
    print(f'  {n:5} {name}')
    for line, source in examples[name][:2]:
        print(f'        {source}: {line[:120]}')
