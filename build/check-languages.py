#!/usr/bin/env python3
"""
Structural check for the shipped language packs.

English is the master. It lives in Localization/Loc.cs as a C# dictionary, and every
pack under Languages/*.json is the same set of keys with the words replaced. A pack
that drops a key falls back to English for it, silently, and nobody notices until a
user sees one English line in an otherwise translated window - so the gap is worth a
build failure rather than a shrug.

What is checked, and why each one is a real bug rather than a style nit:

  missing / unknown keys   A missing key shows English at runtime. An unknown key is
                           either a typo or a string that no longer exists.
  placeholder parity       "{0} of {1}" translated as "{0}" loses a number. Worse,
                           string.Format throws on an index the arguments do not
                           reach, and Loc.T swallows that and returns the raw
                           template - so the user sees "{0}" on screen.
  line break parity        A multi-line message flattened into one paragraph loses
                           the blank line the dialog lays its question out around.
  untranslated values      A value byte-identical to English is usually a key the
                           translator skipped. Short technical strings legitimately
                           match, so this is a warning with a count, not a failure.

    python build/check-languages.py            # every pack
    python build/check-languages.py de fr      # only these

Exit code is 1 when anything is wrong, so it can be wired into CI.
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOC = os.path.join(ROOT, 'src', 'Hexnest.Core', 'Localization', 'Loc.cs')
PACKS = os.path.join(ROOT, 'src', 'Hexnest.Core', 'Languages')

PLACEHOLDER = re.compile(r'\{(\d+)\}')

# Values that are legitimately the same in every language: product names, units,
# command names, and the handful of one-word labels that are loanwords everywhere.
SHARED_VALUES = {
    'Hexnest', 'Windows', 'macOS', 'GitHub', 'MIT', 'BIOS', 'CPU', 'GPU', 'RAM',
    'PID', 'TCP', 'UDP', 'INF', 'USB', 'Wi-Fi', 'VPN', 'launchd', 'pnputil',
    'nettop', 'Rosetta', 'Xcode', 'Homebrew', '.NET', 'SHA-256', 'Swap',
}


def english_master() -> dict:
    """The English dictionary out of Loc.cs, which is the source of truth."""
    with open(LOC, encoding='utf-8') as handle:
        source = handle.read()

    start = source.index('English = new(StringComparer.Ordinal)')
    end = source.index('\n    };', start)

    pairs = re.findall(r'\["([^"]+)"\]\s*=\s*"((?:[^"\\]|\\.)*)"', source[start:end])

    # The master is read out of C# source, where a newline is the two characters \ and
    # n. A JSON pack's "\n" has already been decoded to a real newline by json.load.
    # Without this the two are never comparable and every multi-line string looks wrong.
    def unescape(value: str) -> str:
        return (value.replace('\\n', '\n')
                     .replace('\\t', '\t')
                     .replace('\\"', '"')
                     .replace('\\\\', '\\'))

    return {key: unescape(value) for key, value in pairs}


def newlines(value: str) -> int:
    """How many line breaks a value carries, after both sides are decoded."""
    return value.count('\n')


def check(code: str, english: dict) -> list:
    path = os.path.join(PACKS, code + '.json')
    problems = []

    if not os.path.exists(path):
        return ['%s: no pack at Languages/%s.json' % (code, code)]

    try:
        with open(path, encoding='utf-8') as handle:
            pack = json.load(handle)
    except (ValueError, UnicodeDecodeError) as error:
        return ['%s: not valid UTF-8 JSON - %s' % (code, error)]

    for meta in ('_name', '_englishName'):
        if not pack.get(meta):
            problems.append('%s: missing the %s metadata key, so the language would '
                            'not appear in the picker' % (code, meta))

    keys = {k for k in pack if not k.startswith('_')}

    missing = sorted(set(english) - keys)
    unknown = sorted(keys - set(english))

    if missing:
        problems.append('%s: %d key(s) missing, which fall back to English: %s%s'
                        % (code, len(missing), ', '.join(missing[:8]),
                           ' ...' if len(missing) > 8 else ''))
    if unknown:
        problems.append('%s: %d key(s) no longer exist: %s%s'
                        % (code, len(unknown), ', '.join(unknown[:8]),
                           ' ...' if len(unknown) > 8 else ''))

    same = 0

    for key in sorted(set(english) & keys):
        source, target = english[key], pack[key]

        if not isinstance(target, str):
            problems.append('%s: %s is not a string' % (code, key))
            continue

        if not target.strip():
            problems.append('%s: %s is empty' % (code, key))
            continue

        wanted = set(PLACEHOLDER.findall(source))
        got = set(PLACEHOLDER.findall(target))

        if wanted != got:
            problems.append(
                '%s: %s placeholder mismatch - English has {%s}, translation has {%s}'
                % (code, key,
                   '}{'.join(sorted(wanted)) or '-',
                   '}{'.join(sorted(got)) or '-'))

        if newlines(source) != newlines(target):
            problems.append(
                '%s: %s has %d line break(s) in English but %d in the translation'
                % (code, key, newlines(source), newlines(target)))

        if target == source and source not in SHARED_VALUES:
            same += 1

    if same:
        ratio = same * 100.0 / max(1, len(english))
        note = ('%s: NOTE %d value(s) are identical to English (%.0f%%)'
                % (code, same, ratio))
        # Above a fifth it stops looking like coincidence and starts looking like a
        # translator who pasted the master file.
        problems.append(note if ratio > 20 else None)

    return [p for p in problems if p]


def main(argv: list) -> int:
    english = english_master()

    if argv:
        codes = argv
    else:
        codes = sorted(f[:-5] for f in os.listdir(PACKS) if f.endswith('.json'))

    print('English master: %d keys' % len(english))

    failing = 0

    for code in codes:
        problems = check(code, english)
        hard = [p for p in problems if 'NOTE' not in p]

        if hard:
            failing += 1
            for problem in problems:
                print('FAIL ' + problem)
        else:
            path = os.path.join(PACKS, code + '.json')
            with open(path, encoding='utf-8') as handle:
                pack = json.load(handle)
            print('%s: OK  %d keys  (%s)'
                  % (code, len([k for k in pack if not k.startswith('_')]),
                     pack.get('_name', '?')))
            for problem in problems:
                print('     ' + problem)

    print('\npacks checked: %d, failing: %d' % (len(codes), failing))
    return 1 if failing else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
