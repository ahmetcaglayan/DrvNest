#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Structural check for the translated READMEs.

Same idea as build/check-site.py: the five READMEs are one document in five languages, so
everything except the words has to match. A translator who drops a table row, renames a
heading anchor, breaks a badge URL or edits a code fence produces a file that looks fine
in a prose diff and is wrong.

    python build/check-readme.py          # check every language
    python build/check-readme.py ru zh    # check only these

Exit code is 1 when anything is wrong.
"""
from __future__ import annotations

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LANGUAGES = ['tr', 'ru', 'zh', 'hi', 'pt', 'ja', 'de', 'fr', 'ko']

FENCE = re.compile(r'^```')
HEADING = re.compile(r'^(#{1,6})\s')
IMAGE = re.compile(r'!\[[^\]]*\]\(([^)]+)\)|<img\s[^>]*src="([^"]+)"')
LINK = re.compile(r'(?<!!)\[[^\]]*\]\(([^)]+)\)')


def read(path: str) -> str:
    with open(path, encoding='utf-8') as handle:
        return handle.read()


def outside_fences(text: str):
    """Yields (index, line) for every line that is not inside a code fence."""
    inside = False
    for i, line in enumerate(text.splitlines()):
        if FENCE.match(line.strip()):
            inside = not inside
            continue
        if not inside:
            yield i, line


def shape(text: str) -> dict:
    headings = []
    table_rows = 0
    table_widths = []

    for _i, line in outside_fences(text):
        match = HEADING.match(line)
        if match:
            headings.append(len(match.group(1)))

        stripped = line.strip()
        if stripped.startswith('|') and stripped.endswith('|'):
            table_rows += 1
            table_widths.append(stripped.count('|'))

    fences = [line for line in text.splitlines() if FENCE.match(line.strip())]

    images = []
    for match in IMAGE.finditer(text):
        images.append(match.group(1) or match.group(2))

    # Only the targets matter; the label is prose.
    links = [m.group(1) for m in LINK.finditer(text)]

    return {
        'headingLevels': headings,
        'tableRows': table_rows,
        'tableWidths': table_widths,
        'fences': len(fences),
        'images': images,
        'links': sorted(set(links)),
    }


def compare(reference: str, candidate: str, code: str) -> list[str]:
    problems: list[str] = []

    a, b = shape(reference), shape(candidate)

    if a['headingLevels'] != b['headingLevels']:
        problems.append(
            '%s: heading structure differs - English has %d headings %s, %s has %d %s'
            % (code, len(a['headingLevels']), a['headingLevels'][:14],
               code, len(b['headingLevels']), b['headingLevels'][:14]))

    if a['tableRows'] != b['tableRows']:
        problems.append('%s: %d table rows in English, %d in the translation'
                        % (code, a['tableRows'], b['tableRows']))
    elif a['tableWidths'] != b['tableWidths']:
        for i, (left, right) in enumerate(zip(a['tableWidths'], b['tableWidths'])):
            if left != right:
                problems.append('%s: table row %d has %d columns in English, %d here'
                                % (code, i, left - 1, right - 1))
                break

    if a['fences'] != b['fences']:
        problems.append('%s: %d code-fence markers in English, %d here (unbalanced fence?)'
                        % (code, a['fences'], b['fences']))
    elif b['fences'] % 2:
        problems.append('%s: odd number of code-fence markers - a fence is not closed' % code)

    if a['images'] != b['images']:
        missing = [x for x in a['images'] if x not in b['images']]
        extra = [x for x in b['images'] if x not in a['images']]
        problems.append('%s: image paths differ - missing %s, unexpected %s'
                        % (code, missing[:5] or 'none', extra[:5] or 'none'))

    for image in b['images']:
        if image.startswith(('http://', 'https://')):
            continue
        if not os.path.exists(os.path.join(ROOT, image.replace('/', os.sep))):
            problems.append('%s: image not found on disk: %s' % (code, image))

    # The language row must link the other four and not itself.
    for other in ['', 'tr', 'ru', 'zh', 'hi', 'pt', 'ja', 'de', 'fr', 'ko']:
        target = 'README.md' if other == '' else 'README.%s.md' % other
        if other == code:
            if '(%s)' % target in candidate:
                problems.append('%s: links to itself in the language row' % code)
        elif '(%s)' % target not in candidate:
            problems.append('%s: the language row does not link %s' % (code, target))

    # Relative links must still resolve.
    for link in b['links']:
        if link.startswith(('http://', 'https://', '#', 'mailto:', '../')):
            continue
        target = os.path.join(ROOT, link.split('#')[0].replace('/', os.sep))
        if not os.path.exists(target):
            problems.append('%s: relative link does not resolve: %s' % (code, link))

    return problems


def main() -> int:
    wanted = sys.argv[1:] or LANGUAGES
    reference = read(os.path.join(ROOT, 'README.md'))

    failures = 0

    for code in wanted:
        path = os.path.join(ROOT, 'README.%s.md' % code)

        if not os.path.exists(path):
            print('%s: FAIL missing %s' % (code, path))
            failures += 1
            continue

        problems = compare(reference, read(path), code)

        if problems:
            failures += 1
            for problem in problems:
                print('FAIL ' + problem)
        else:
            print('%s: OK  same structure as README.md' % code)

    print()
    print('languages checked: %d, failing: %d' % (len(wanted), failures))
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
