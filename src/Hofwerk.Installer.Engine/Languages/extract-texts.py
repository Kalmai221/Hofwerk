#!/usr/bin/env python3
"""Lists every English text of Hofwerk that a language file can translate: the texts in the layout files and in the code.

  python src/Hofwerk.Installer.Engine/Languages/extract-texts.py            prints the list
  python src/Hofwerk.Installer.Engine/Languages/extract-texts.py de         prints what de.json is still missing
  python src/Hofwerk.Installer.Engine/Languages/extract-texts.py --check    every language: missing texts,
                                                                           and {0} placeholders that differ; exit code 1 if anything is wrong

A text is found where the layout files give a control its words (Text, Content, Header, Title, ToolTip ...), and where the code calls
Localizer.T, Localizer.F or Localizer.Count. The tests use the same rules, so a new text with no translation is noticed.
"""
import glob
import html
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
SRC = os.path.join(ROOT, 'src')

LAYOUT_FILES = [
    'Hofwerk.WinUI/MainWindow.xaml',
    'Hofwerk.Linux/Views/MainWindow.axaml',
    'Hofwerk.Installer/MainWindow.axaml',
]
ATTRIBUTES = r'(?:Text|Content|Header|PlaceholderText|Label|Title|OnContent|OffContent|Description|ToolTip\.Tip|ToolTipService\.ToolTip|AutomationProperties\.Name|PrimaryButtonText|CloseButtonText|SecondaryButtonText|Watermark)'
LAYOUT = re.compile(ATTRIBUTES + r'="([^"{][^"]*)"')
RUN_TEXT = re.compile(r'<Run[^>]*\bText="([^"{][^"]*)"')

STRING = r'"((?:[^"\\]|\\.)*)"'
CODE_PROPERTY = re.compile(r'\b(?:Content|Text|Header|PlaceholderText|Title|PrimaryButtonText|CloseButtonText|SecondaryButtonText|Watermark|Label)\s*=\s*' + STRING)
SET_NAME = re.compile(r'AutomationProperties\.SetName\([^,]+,\s*' + STRING)
LOCALIZER_TF = re.compile(r'Localizer\.(?:T|F)\(\s*' + STRING)
LOCALIZER_COUNT = re.compile(r'Localizer\.Count\([^,]+,\s*' + STRING + r'\s*,\s*' + STRING)
TERNARY = re.compile(r'Localizer\.T\(\s*[^"()]*\?\s*' + STRING + r'\s*:\s*' + STRING)
SECTION = re.compile(r'"(?:home|appsettings|settings|destinations|busstops|routes)" => \(' + STRING + r',\s*' + STRING)
STEPS = re.compile(r'new\(InstallStep\.\w+,\s*' + STRING)

# files whose code builds the dialogs, windows and installer pages (the layout of these is translated when they open)
CODE_WITH_CONTROLS = [
    'Hofwerk.WinUI/MainWindow.xaml.cs', 'Hofwerk.WinUI/ErrorHandler.cs',
    'Hofwerk.Linux/Views/NewFileWindow.cs', 'Hofwerk.Linux/Views/ImportWindow.cs', 'Hofwerk.Linux/Views/ExportWindow.cs',
    'Hofwerk.Linux/Views/PresetImportWindow.cs', 'Hofwerk.Linux/Views/ErrorWindow.cs', 'Hofwerk.Linux/Views/UnsavedChangesWindow.cs',
    'Hofwerk.Linux/Views/WhatsNewWindow.cs', 'Hofwerk.Linux/Views/MainWindow.axaml.cs',
    'Hofwerk.Installer/InstallerViewModel.cs',
]
SKIP_FILES = ('Diagnostics', 'DevTools')   # a window for developers is not translated
NOT_TRANSLATED = {'Open settings folder', 'Open crash log', 'Clear recent files', 'Simulate an error', 'Diagnostics', 'Diagnostics report', 'Copied', 'No crash log', 'Cleared',
                  'What Hofwerk knows about itself. Your user name is taken out of the paths, so this can be pasted into a bug report.'}
NOT_TRANSLATED = {'Open settings folder', 'Open crash log', 'Clear recent files', 'Simulate an error', 'Diagnostics', 'Diagnostics report', 'Copied', 'No crash log', 'Cleared',
                  'What Hofwerk knows about itself. Your user name is taken out of the paths, so this can be pasted into a bug report.'}


def unescape_cs(text):
    return text.encode('utf-8').decode('unicode_escape').encode('latin-1', 'ignore').decode('utf-8', 'ignore') if '\\' in text else text


def clean(text):
    return text.replace('\\"', '"').replace('\\n', '\n').replace('\\\\', '\\')


def looks_like_text(text):
    t = text.strip()
    if len(t) < 2 or not re.search(r'[A-Za-z]{2}', t):
        return False
    if re.fullmatch(r'[\w./\\:%-]+', t) and ' ' not in t and not t[0].isupper():
        return False                      # an identifier or a path
    if t.startswith(('Cascadia', 'Segoe', 'ms-appx', 'http', '/', '#')):
        return False
    if t in ('Hofwerk', 'OMSI', 'IBIS', 'GitHub', 'HOF', 'WinUI', 'Avalonia'):
        return False
    return True


def read(path):
    with open(path, encoding='utf-8-sig', errors='ignore') as f:
        return f.read()


def extract():
    keys = {}

    def add(text, where):
        text = clean(text) if '\\' in text else text
        if looks_like_text(text) and text not in NOT_TRANSLATED:
            keys.setdefault(text, where)

    for rel in LAYOUT_FILES:
        text = read(os.path.join(SRC, rel))
        for m in list(LAYOUT.finditer(text)) + list(RUN_TEXT.finditer(text)):
            add(html.unescape(m.group(1)), rel)

    for path in glob.glob(os.path.join(SRC, '**', '*.cs'), recursive=True):
        rel = os.path.relpath(path, SRC).replace(os.sep, '/')
        if '/obj/' in '/' + rel or '/bin/' in '/' + rel or any(s in rel for s in SKIP_FILES) or rel.endswith('.g.cs'):
            continue
        text = read(path)
        for m in LOCALIZER_TF.finditer(text):
            add(m.group(1), rel)
        for m in LOCALIZER_COUNT.finditer(text):
            add(m.group(1), rel)
            add(m.group(2), rel)
        for m in TERNARY.finditer(text):
            add(m.group(1), rel)
            add(m.group(2), rel)
        if rel in CODE_WITH_CONTROLS:
            for m in CODE_PROPERTY.finditer(text):
                add(m.group(1), rel)
            for m in SET_NAME.finditer(text):
                add(m.group(1), rel)
            for m in SECTION.finditer(text):
                add(m.group(1), rel)
                add(m.group(2), rel)
            for m in STEPS.finditer(text):
                add(m.group(1), rel)
    return keys


def check(keys):
    folder = os.path.dirname(__file__)
    placeholders = lambda t: sorted(re.findall(r'\{\d+(?::[^}]*)?\}', t))
    problems = 0
    for path in sorted(glob.glob(os.path.join(folder, '*.json'))):
        code = os.path.basename(path)[:-5]
        if code == 'en':
            continue
        have = json.load(open(path, encoding='utf-8'))['strings']
        for key, where in sorted(keys.items()):
            if key not in have:
                print(f'{code}: missing   {key!r} ({where})'); problems += 1
            else:
                value = have[key]
                for form in (value if isinstance(value, list) else [value]):      # a language that counts in more ways has a list of texts
                    if placeholders(key) != placeholders(form):
                        print(f'{code}: placeholders differ   {key!r}'); problems += 1
        for key in sorted(set(have) - set(keys)):
            print(f'{code}: note, not found by this script (fine if the code builds it another way)   {key!r}')
    print(f'-- {problems} problem(s)', file=sys.stderr)
    return 1 if problems else 0


def main():
    keys = extract()
    if sys.argv[1:] == ['--check']:
        sys.exit(check(keys))
    if len(sys.argv) > 1:
        language = os.path.join(os.path.dirname(__file__), sys.argv[1] + '.json')
        have = json.load(open(language, encoding='utf-8'))['strings']
        missing = {k: w for k, w in keys.items() if k not in have}
        for key, where in sorted(missing.items(), key=lambda kv: (kv[1], kv[0])):
            print(f'{where}\t{key}')
        print(f'-- {len(missing)} of {len(keys)} texts have no translation in {sys.argv[1]}.json', file=sys.stderr)
    else:
        for key, where in sorted(keys.items(), key=lambda kv: (kv[1], kv[0])):
            print(f'{where}\t{key}')
        print(f'-- {len(keys)} texts', file=sys.stderr)


if __name__ == '__main__':
    main()
