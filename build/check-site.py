#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Structural check for the translated landing pages.

The 10 language pages are the same document in 10 languages. Everything except the
words has to match: the same elements in the same order, the same ids and classes, the
same links, the same images, the same inline SVG geometry. A translator who drops a
</div>, renames a section id or forgets to fix a relative path breaks the page in a way
that is invisible in a diff of prose.

This compares each translation against docs/site/index.html and reports what differs.

    python build/check-site.py            # check every language
    python build/check-site.py ru zh      # check only these

Exit code is 1 when anything is wrong, so it can be wired into CI.
"""
from __future__ import annotations

import os
import re
import sys
from html.parser import HTMLParser

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(ROOT, 'docs', 'site')

LANGUAGES = ['tr', 'ru', 'zh', 'hi', 'pt', 'ja', 'de', 'fr', 'ko']

# Attributes that carry structure rather than words.
STRUCTURAL = ('id', 'class', 'role', 'type', 'name', 'property', 'rel', 'data-slide',
              'viewBox', 'd', 'x', 'y', 'width', 'height', 'rx', 'points', 'src')

# Attributes whose value is a path that must differ between the root page and a
# sub-folder page, so they are compared after normalising the "../" prefix.
PATHS = ('href', 'src')


class Skeleton(HTMLParser):
    """Collects the structural shape of a document, ignoring all text."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.tags: list[str] = []
        self.ids: list[str] = []
        self.classes: list[str] = []
        self.images: list[str] = []
        self.links: list[str] = []
        self.attrs: list[tuple[str, str]] = []
        self.slides: list[str] = []
        self.depth = 0
        self.max_depth = 0

    def handle_starttag(self, tag, attrs):
        self.tags.append(tag)
        mapping = dict(attrs)

        if 'id' in mapping:
            self.ids.append(mapping['id'])
        if 'class' in mapping:
            self.classes.append(mapping['class'])

        if tag == 'img' and 'src' in mapping:
            self.images.append(normalise_path(mapping['src']))

        if tag == 'a' and 'href' in mapping:
            self.links.append(normalise_path(mapping['href']))

        for key in STRUCTURAL:
            if key in mapping and key not in PATHS:
                self.attrs.append((tag + '.' + key, mapping[key]))

        # The carousel slides are labelled "3 / 12" for screen readers. The label is
        # a count, not prose, so it is the same in every language and it has to keep
        # up when a screenshot is added. Whole aria-labels are not compared: the rest
        # of them are words, and words are translated.
        if tag == 'figure' and 'shot' in mapping.get('class', '').split():
            self.slides.append(mapping.get('aria-label', ''))

        if tag not in ('img', 'br', 'hr', 'meta', 'link', 'input', 'source', 'path',
                       'rect', 'line', 'circle', 'use', 'stop'):
            self.depth += 1
            self.max_depth = max(self.max_depth, self.depth)

    def handle_endtag(self, tag):
        if tag not in ('img', 'br', 'hr', 'meta', 'link', 'input', 'source'):
            self.depth -= 1


def normalise_path(value: str) -> str:
    """`../style.css` and `style.css` are the same asset from two directories."""
    value = value.strip()
    if value.startswith('../'):
        value = value[3:]
    return value


def read(path: str) -> str:
    with open(path, encoding='utf-8') as handle:
        return handle.read()


VERSIONED = [
    ('schema.org softwareVersion', re.compile(r'"softwareVersion":\s*"([^"]+)"')),
    ('the "recently added" badge', re.compile(r'<span class="badge-new">([^<]+)</span>')),
]


def versions(text: str) -> dict:
    """The version numbers a page states about the application."""
    found = {}
    for label, pattern in VERSIONED:
        match = pattern.search(text)
        found[label] = match.group(1).strip() if match else None
    return found


LD = re.compile(r'<script type="application/ld\+json">(.*?)</script>', re.S)

# Fields inside the JSON-LD that are prose and must therefore be translated. The list is
# deliberately not exhaustive: names, URLs and version numbers are the same everywhere.
TRANSLATED = ['description', 'applicationSubCategory', 'softwareRequirements', 'permissions']


def structured_data(text: str) -> list:
    """The parsed JSON-LD blocks, or an empty list when they do not parse."""
    import json

    blocks = []
    for raw in LD.findall(text):
        try:
            blocks.append(json.loads(raw))
        except ValueError:
            return []
    return blocks


def compare_structured_data(reference: str, candidate: str, code: str) -> list[str]:
    """
    Checks the JSON-LD, which the structural comparison cannot see.

    Everything below lives inside a <script> body, so to the skeleton it is one text
    node and nothing more. That blind spot let two separate faults sit in the published
    site across two releases: the four oldest translations described a Windows-only
    driver updater long after the Mac build shipped, listing no macOS in
    operatingSystem at all, and the five newest pages carried the English payload
    wholesale - twenty-five of twenty-six features, eight FAQ answers - under a fully
    translated page. Search results are built from exactly this.
    """
    problems: list[str] = []

    left, right = structured_data(reference), structured_data(candidate)

    if not right:
        return ['%s: the JSON-LD does not parse' % code]

    if len(left) != len(right):
        return ['%s: %d JSON-LD blocks in English, %d here' % (code, len(left), len(right))]

    for i, (a, b) in enumerate(zip(left, right)):
        for field in TRANSLATED:
            if field not in a:
                continue
            if field not in b:
                problems.append('%s: JSON-LD is missing %s' % (code, field))
            elif a[field] == b[field]:
                problems.append('%s: JSON-LD %s is still the English text' % (code, field))

        if 'featureList' in a:
            other = b.get('featureList') or []
            if len(other) != len(a['featureList']):
                problems.append('%s: featureList has %d entries, English has %d'
                                % (code, len(other), len(a['featureList'])))
            else:
                same = sum(1 for x, y in zip(a['featureList'], other) if x == y)
                if same:
                    problems.append('%s: %d of %d featureList entries are still English'
                                    % (code, same, len(other)))

        if 'operatingSystem' in a and 'macOS' in str(a['operatingSystem']) \
                and 'macOS' not in str(b.get('operatingSystem', '')):
            problems.append('%s: operatingSystem does not mention macOS' % code)

        if 'mainEntity' in a:
            questions = b.get('mainEntity') or []
            if len(questions) != len(a['mainEntity']):
                problems.append('%s: %d FAQ entries, English has %d'
                                % (code, len(questions), len(a['mainEntity'])))
            else:
                same = sum(1 for x, y in zip(a['mainEntity'], questions)
                           if x.get('name') == y.get('name'))
                if same:
                    problems.append('%s: %d of %d FAQ questions are still English'
                                    % (code, same, len(questions)))

    return problems


def compare(reference: str, candidate: str, code: str) -> list[str]:
    problems: list[str] = compare_structured_data(reference, candidate, code)

    # Versions first, because this is the failure the structural check cannot see and
    # the one that actually reaches a reader. Releasing 1.4.0 bumped the English page
    # and left four translations claiming 1.3.0 was the newest release - identical
    # markup, different facts - and every structural check passed the whole time.
    for label, expected in versions(reference).items():
        actual = versions(candidate)[label]
        if expected != actual:
            problems.append('%s: %s says %r, English says %r'
                            % (code, label, actual, expected))

    a, b = Skeleton(), Skeleton()
    a.feed(reference)
    b.feed(candidate)

    if a.tags != b.tags:
        # Report the first divergence: after one missing tag everything shifts, and a
        # list of four hundred differences helps nobody.
        for i, (left, right) in enumerate(zip(a.tags, b.tags)):
            if left != right:
                problems.append(
                    '%s: element order diverges at #%d - English has <%s>, %s has <%s>'
                    % (code, i, left, code, right))
                break
        else:
            problems.append('%s: %d elements in English, %d in the translation'
                            % (code, len(a.tags), len(b.tags)))

    for label, left, right in (('ids', a.ids, b.ids),
                               ('classes', a.classes, b.classes),
                               ('images', a.images, b.images)):
        if left != right:
            missing = [x for x in left if x not in right]
            extra = [x for x in right if x not in left]
            problems.append('%s: %s differ - missing %s, unexpected %s'
                            % (code, label, missing[:6] or 'none', extra[:6] or 'none'))

    if a.attrs != b.attrs:
        for i, (left, right) in enumerate(zip(a.attrs, b.attrs)):
            if left != right:
                problems.append('%s: structural attribute #%d differs - English %r, %s %r'
                                % (code, i, left, code, right))
                break

    # The slide labels have to agree with the English page and, more importantly, with
    # the number of slides actually in the carousel: adding a screenshot without fixing
    # them tells a screen reader there are ten slides and then hands it a twelfth.
    if a.slides != b.slides:
        for i, (left, right) in enumerate(zip(a.slides, b.slides)):
            if left != right:
                problems.append('%s: screenshot slide #%d is labelled %r, English says %r'
                                % (code, i + 1, right, left))
                break
        else:
            problems.append('%s: %d screenshot slides in English, %d in the translation'
                            % (code, len(a.slides), len(b.slides)))

    for i, label in enumerate(b.slides):
        wanted = '%d / %d' % (i + 1, len(b.slides))
        if label != wanted:
            problems.append('%s: screenshot slide #%d is labelled %r, should be %r'
                            % (code, i + 1, label, wanted))
            break

    # The <html lang> must match the folder.
    match = re.search(r'<html[^>]*\blang="([^"]+)"', candidate)
    if not match:
        problems.append('%s: <html> has no lang attribute' % code)
    elif match.group(1) != code:
        problems.append('%s: <html lang="%s"> should be "%s"' % (code, match.group(1), code))

    # Assets must be reachable from the sub-folder.
    for image in b.images:
        target = os.path.join(SITE, image.replace('/', os.sep))
        if not os.path.exists(target):
            problems.append('%s: image not found on disk: %s' % (code, image))

    if 'style.css' not in candidate:
        problems.append('%s: does not link the stylesheet' % code)

    # Every page must point at every other language.
    for other in ['en'] + LANGUAGES:
        if other == code:
            continue
        expected = '../' if other == 'en' else '../%s/' % other
        if 'hreflang="%s"' % other not in candidate:
            problems.append('%s: no link to the %s page' % (code, other))
        elif expected not in candidate:
            problems.append('%s: the %s link is not the expected relative path %r'
                            % (code, other, expected))

    # The language menu must offer all five languages exactly once, and the footer must
    # list the other four. Comparing the two pages structurally does not catch a mistake
    # made identically on both, which is exactly how a menu with three duplicated entries
    # and an empty footer list survived a full structural pass.
    menu = re.search(r'<details class="lang-menu">.*?</details>', candidate, re.S)

    if not menu:
        problems.append('%s: no language menu' % code)
    else:
        offered = re.findall(r'hreflang="([a-z]{2})"', menu.group(0))
        expected = ['en'] + LANGUAGES

        if sorted(offered) != sorted(expected):
            problems.append('%s: the language menu offers %s, expected each of %s once'
                            % (code, offered, expected))

        marked = re.findall(r'hreflang="([a-z]{2})"[^>]*aria-current="true"', menu.group(0))
        marked += re.findall(r'aria-current="true"[^>]*hreflang="([a-z]{2})"', menu.group(0))

        if marked != [code]:
            problems.append('%s: the language menu marks %s as current, expected [%r]'
                            % (code, marked, code))

    footer = candidate[candidate.rindex('<footer'):]
    in_footer = re.findall(r'hreflang="([a-z]{2})"', footer)

    if sorted(in_footer) != sorted(x for x in expected if x != code):
        problems.append('%s: the footer links %s, expected the other four languages'
                        % (code, in_footer))

    if 'shotsTrack' not in candidate:
        problems.append('%s: the screenshot carousel is missing' % code)

    return problems


def main() -> int:
    wanted = sys.argv[1:] or LANGUAGES
    reference = read(os.path.join(SITE, 'index.html'))

    failures = 0

    for code in wanted:
        path = os.path.join(SITE, code, 'index.html')

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
            print('%s: OK  same structure as the English page' % code)

    print()
    print('languages checked: %d, failing: %d' % (len(wanted), failures))
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
