#!/usr/bin/env python3
"""Keeps the language selector at the top of the documents in step with the languages that have a folder here.

  python translations/update-language-links.py           rewrites the selectors
  python translations/update-language-links.py --check    changes nothing; exit code 1 when a selector is out of date (for CI)

A language is a folder translations/<code>/ (de, pt-BR ...) with any of README.md, ROADMAP.md and Presets.md in it. Its name, as shown in
the selector, is the "name" of src/Hofwerk.Installer.Engine/Languages/<code>.json. The English documents are the ones at the top
of the repository (README.md, ROADMAP.md, Presets/README.md). Every document gets one line between the markers

  <!-- languages -->  ...  <!-- /languages -->

that links to the same document in each language that has it, with the language of the document itself in bold and not a link. A
document that has no markers yet gets them: at the very top of README.md, and under the title of the other documents.
"""
import glob
import json
import os
import re
import sys
from urllib.parse import quote

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
FOLDER = os.path.join(ROOT, 'translations')
LANGUAGE_FILES = os.path.join(ROOT, 'src', 'Hofwerk.Installer.Engine', 'Languages')

# kind -> where the English document is, and what the translated one is called inside translations/<code>/
DOCUMENTS = {
    'README': ('README.md', 'README.md'),
    'ROADMAP': ('ROADMAP.md', 'ROADMAP.md'),
    'Presets': ('Presets/README.md', 'Presets.md'),
}
START, END = '<!-- languages -->', '<!-- /languages -->'


def language_name(code):
    path = os.path.join(LANGUAGE_FILES, code + '.json')
    try:
        with open(path, encoding='utf-8') as f:
            return json.load(f).get('name') or code
    except (OSError, ValueError):
        return code


def languages():
    """['en', 'de', ...]: English first, then every folder here, by code."""
    return ['en'] + sorted(d for d in os.listdir(FOLDER) if os.path.isdir(os.path.join(FOLDER, d)))


def path_of(kind, code):
    english, translated = DOCUMENTS[kind]
    return os.path.join(ROOT, english) if code == 'en' else os.path.join(FOLDER, code, translated)


def relative(from_file, to_file):
    return quote(os.path.relpath(to_file, os.path.dirname(from_file)).replace(os.sep, '/'), safe='/-_.~')


def selector(kind, code):
    me = path_of(kind, code)
    parts = []
    for other in languages():
        target = path_of(kind, other)
        if not os.path.isfile(target):
            continue
        name = 'English' if other == 'en' else language_name(other)
        parts.append('<b>%s</b>' % name if other == code else '<a href="%s">%s</a>' % (relative(me, target), name))
    return '%s\n<p align="center">%s</p>\n%s' % (START, ' · '.join(parts), END)


def update(path, kind, code, check):
    with open(path, encoding='utf-8', newline='') as f:
        text = f.read()
    eol = '\r\n' if '\r\n' in text else '\n'
    text = text.replace('\r\n', '\n')
    block = selector(kind, code)

    if START in text and END in text:
        new = re.sub(re.escape(START) + r'.*?' + re.escape(END), lambda _: block, text, count=1, flags=re.S)
    else:
        # an older one-line selector ("[English](...) · [Deutsch](...)") goes
        text = re.sub(r'(?m)^\[\**English\**\]\([^)]*\) · .*\n\n?', '', text)
        if kind == 'README':
            new = block + '\n\n' + text
        else:
            lines = text.split('\n')
            at = next((i for i, l in enumerate(lines) if l.startswith('# ')), -1) + 1
            lines[at:at] = ['', block]
            new = '\n'.join(lines)
    changed = new != text
    if changed and not check:
        with open(path, 'w', encoding='utf-8', newline='') as f:
            f.write(new.replace('\n', eol))
    return changed


def main():
    check = '--check' in sys.argv[1:]
    outdated = []
    for kind in DOCUMENTS:
        for code in languages():
            path = path_of(kind, code)
            if os.path.isfile(path) and update(path, kind, code, check):
                outdated.append(os.path.relpath(path, ROOT))
    for path in outdated:
        print(('out of date: ' if check else 'updated: ') + path)
    if check and outdated:
        print('Run: python translations/update-language-links.py', file=sys.stderr)
        sys.exit(1)
    if not outdated:
        print('-- the language selectors are up to date', file=sys.stderr)


if __name__ == '__main__':
    main()
