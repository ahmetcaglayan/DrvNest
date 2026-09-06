#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Extracts the English string table from Loc.cs as JSON.

The starting point for a new translation: run this, translate the values, save the result
as src/DrvNest.App/Languages/<code>.json with "_name" and "_englishName" added, and the
next build embeds it. See docs/ARCHITECTURE.md, "Adding a language".

    python build/extract-strings.py > de.json
    python build/extract-strings.py de.json
"""
import io, json, re, sys

import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
src = os.path.join(ROOT, 'src', 'DrvNest.App', 'Services', 'Loc.cs')
out = sys.argv[1] if len(sys.argv) > 1 else None

s = io.open(src, encoding='utf-8').read()

start = s.index('private static readonly Dictionary<string, string> English')
end = s.index('private static readonly Dictionary<string, string> Turkish')
block = s[start:end]

# ["key"] = "value",   with C# escapes inside the value.
pattern = re.compile(r'\["([^"]+)"\]\s*=\s*"((?:[^"\\]|\\.)*)"\s*,?')

pairs = []
for m in pattern.finditer(block):
    key = m.group(1)
    value = m.group(2)
    value = value.replace('\\"', '"').replace('\\\\', '\\').replace('\\n', '\n').replace('\\t', '\t')
    pairs.append((key, value))

data = dict(pairs)
assert len(data) == len(pairs), 'duplicate keys in the English table'

io.open(out, 'w', encoding='utf-8', newline='\n').write(
    json.dumps(data, ensure_ascii=False, indent=2) + '\n')

print('%d keys -> %s' % (len(data), out))
