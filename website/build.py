#!/usr/bin/env python3
"""Builds the Hofwerk website into a folder of plain files (no framework, nothing to install).

  python website/build.py --out _site                    on GitHub Actions: uses the real releases
  python website/build.py --out _site --dist dist        on a computer: pretends the installers in dist/ are the newest release

Pages that come from files in the repository, so there is one place to edit each:
  What's new       CHANGELOG.md
  Roadmap          ROADMAP.md
  Compare          the "Hofwerk and HOF Suite" part of README.md
  String presets   Presets/README.md
The Download page lists the newest release (and the newest pre-release) with the installer files copied onto the site itself, so
people download from the website and never need to visit the code host.
"""
import argparse
import datetime
import hashlib
import html
import json
import os
import re
import shutil
import sys
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mdhtml import Converter, section  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SRC = os.path.join(HERE, 'src')

SITE_NAME = 'Hofwerk'
TAGLINE = 'The modern .hof editor for OMSI 2'

# The files of a release that people download. The packs the app updates itself from are not part of the website.
INSTALLER = re.compile(r'^Hofwerk-(?P<kind>Setup|WebSetup)-(?P<version>.+?)-(?P<rid>win-x64|linux-x64)\.(?P<ext>exe|tar\.gz)$')

# The site is built once per language: English at the top, German under /de/. Pages have the same names in both, so the language
# switch links to the same page in the other one.
LANGUAGES = ['en', 'de']
PAGES = {
    'en': [('index.html', 'Home'), ('download.html', 'Download'), ('changelog.html', "What's new"), ('compare.html', 'Compare'),
           ('roadmap.html', 'Roadmap'), ('presets.html', 'String presets')],
    'de': [('index.html', 'Start'), ('download.html', 'Download'), ('changelog.html', 'Neuigkeiten'), ('compare.html', 'Vergleich'),
           ('roadmap.html', 'Roadmap'), ('presets.html', 'String-Vorlagen')],
}
LANGUAGE_NAMES = {'en': 'English', 'de': 'Deutsch'}
ROOT_OF = {'en': '', 'de': '../'}       # how a page of that language gets to the files at the top of the site
SOURCE = {   # the repository file each document page is made from, per language
    'readme': {'en': 'README.md', 'de': 'README.de.md'},
    'roadmap': {'en': 'ROADMAP.md', 'de': 'ROADMAP.de.md'},
    'presets': {'en': 'Presets/README.md', 'de': 'Presets/README.de.md'},
}

# What the Download page says about each file, in German (the English comes from describe()).
DOWNLOAD_TEXT_DE = {
    'linux': ('Linux', '64-Bit, .tar.gz', 'Entpacken und die Datei darin ausführen. Es installiert für dich und fügt Hofwerk deinem Anwendungsmenü hinzu.'),
    'windows-offline': ('Windows-Installer', 'Funktioniert ohne Internet', 'Die ganze App ist darin. Die beste Wahl, wenn du unsicher bist.'),
    'windows-online': ('Windows-Online-Installer', 'Klein, lädt die App herunter', 'Eine kleine Datei, die die App während der Installation herunterlädt. Braucht Internet.'),
}
TEXT = {
    'en': {'latest': 'Latest', 'pre': 'Pre-release', 'pre_title': 'An early version that may have bugs', 'download': 'Download', 'check': 'Check the file (SHA-256)',
           'notes': 'What is in this version',
           'soon': 'The first download is on its way. Come back soon.',
           'intro': 'Hofwerk is free. Pick the file for your system, run it, and Hofwerk is ready. It installs just for you and needs no administrator rights.',
           'early': 'Also show the newest early version (a pre-release that may have bugs)',
           'only_pre': 'Every Hofwerk version so far is a <strong>pre-release</strong>: an early version that may have bugs. Hofwerk becomes <strong>V1</strong> when it can do everything HOF Suite does (see the <a href="roadmap.html">roadmap</a>).',
           'cta': 'Download', 'cta_version': 'Download Hofwerk %s',
           'tag': TAGLINE + '. Edit destinations, bus stops and routes for Windows and Linux.', 'dl_desc': 'Download Hofwerk for Windows or Linux.',
           'whatsnew': "What's new", 'changelog_lead': 'What changed in each version of Hofwerk.', 'changelog_desc': 'What changed in each version of Hofwerk.',
           'compare': 'Hofwerk and HOF Suite', 'compare_desc': 'How Hofwerk compares with the original HOF Suite.',
           'roadmap': 'Roadmap', 'roadmap_desc': 'What is done and what is left before Hofwerk 1.0.',
           'presets': 'String presets', 'presets_desc': 'How to name the string slots of your bus, and how to share your names.',
           'notfound': 'That page is not here', 'back': 'Back to the home page', 'notfound_title': 'Not found', 'download_page': 'Download'},
    'de': {'latest': 'Neueste', 'pre': 'Vorabversion', 'pre_title': 'Eine frühe Version, die Fehler haben kann', 'download': 'Herunterladen', 'check': 'Datei prüfen (SHA-256)',
           'notes': 'Was in dieser Version steckt',
           'soon': 'Der erste Download ist unterwegs. Schau bald wieder vorbei.',
           'intro': 'Hofwerk ist kostenlos. Wähle die Datei für dein System, starte sie, und Hofwerk ist bereit. Es installiert nur für dich und braucht keine Administratorrechte.',
           'early': 'Auch die neueste frühe Version zeigen (eine Vorabversion, die Fehler haben kann)',
           'only_pre': 'Jede Hofwerk-Version bisher ist eine <strong>Vorabversion</strong>: eine frühe Version, die Fehler haben kann. Hofwerk wird <strong>V1</strong>, wenn es alles kann, was die HOF Suite kann (siehe die <a href="roadmap.html">Roadmap</a>).',
           'cta': 'Herunterladen', 'cta_version': 'Hofwerk %s herunterladen',
           'tag': 'Der moderne .hof-Editor für OMSI 2. Ziele, Haltestellen und Routen bearbeiten, für Windows und Linux.', 'dl_desc': 'Hofwerk für Windows oder Linux herunterladen.',
           'whatsnew': 'Neuigkeiten', 'changelog_lead': 'Was sich in jeder Version von Hofwerk geändert hat. Die Notizen sind auf Englisch.',
           'changelog_desc': 'Was sich in jeder Version von Hofwerk geändert hat (auf Englisch).',
           'compare': 'Hofwerk und HOF Suite', 'compare_desc': 'Wie Hofwerk im Vergleich zur originalen HOF Suite abschneidet.',
           'roadmap': 'Roadmap', 'roadmap_desc': 'Was fertig ist und was bis Hofwerk 1.0 noch fehlt.',
           'presets': 'String-Vorlagen', 'presets_desc': 'Wie du die String-Felder deines Busses benennst und deine Namen teilst.',
           'notfound': 'Diese Seite gibt es nicht', 'back': 'Zurück zur Startseite', 'notfound_title': 'Nicht gefunden', 'download_page': 'Download'},
}

# Repository files the documents link to, and where they live on the site.
LINKS = {
    'ROADMAP.md': 'roadmap.html', 'ROADMAP.de.md': 'roadmap.html',
    'CHANGELOG.md': 'changelog.html',
    'Presets/README.md': 'presets.html', 'Presets/README.de.md': 'presets.html',
    'README.md': 'index.html', 'README.de.md': 'index.html',
    '../../releases': 'download.html',
}
IMAGES = {'docs/windows-and-linux.png': 'assets/windows-and-linux.png'}
IMAGES_DE = {'docs/windows-and-linux.png': '../assets/windows-and-linux.png'}


def read(*parts):
    with open(os.path.join(*parts), encoding='utf-8') as f:
        return f.read()


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)


def esc(text):
    return html.escape(str(text), quote=True)


def size_text(size):
    return '%.0f MB' % (size / 1048576) if size >= 10 * 1048576 else '%.1f MB' % (size / 1048576)


# ------------------------------------------------------------------------------------------------------------ releases

def http_get(url, token=None, accept=None):
    request = urllib.request.Request(url, headers={'User-Agent': 'hofwerk-website', **({'Authorization': 'Bearer ' + token} if token else {}),
                                                   **({'Accept': accept} if accept else {})})
    return urllib.request.urlopen(request, timeout=120)


def sha256_of(path):
    digest = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            digest.update(block)
    return digest.hexdigest()


def describe(name):
    """What a file of a release is, for the Download page."""
    m = INSTALLER.match(name)
    if not m:
        return None
    kind, rid = m.group('kind'), m.group('rid')
    if rid == 'linux-x64':
        return {'id': 'linux', 'os': 'linux', 'title': 'Linux', 'sub': '64-bit, .tar.gz', 'order': 3,
                'text': 'Unpack it and run the file inside. It installs for you and adds Hofwerk to your applications menu.'}
    if kind == 'Setup':
        return {'id': 'windows-offline', 'os': 'windows', 'title': 'Windows installer', 'sub': 'Works without internet', 'order': 1,
                'text': 'The whole app is inside. The best choice if you are not sure.'}
    return {'id': 'windows-online', 'os': 'windows', 'title': 'Windows online installer', 'sub': 'Small, downloads the app', 'order': 2,
            'text': 'A small file that downloads the app while it installs. Needs internet.'}


def releases_from_github(repository, out, token):
    """The newest release and the newest pre-release, with their installers copied into <out>/downloads/<tag>/."""
    data = json.load(http_get('https://api.github.com/repos/%s/releases?per_page=30' % repository, token, 'application/vnd.github+json'))
    published = [r for r in data if not r.get('draft')]
    chosen = []
    stable = next((r for r in published if not r.get('prerelease')), None)
    early = next((r for r in published if r.get('prerelease')), None)
    if stable:
        chosen.append(stable)
    if early and (not stable or early['published_at'] > stable['published_at']):
        chosen.append(early)

    result = []
    for release in chosen:
        tag = release['tag_name']
        files = []
        for asset in release.get('assets', []):
            info = describe(asset['name'])
            if not info:
                continue
            target = os.path.join(out, 'downloads', tag, asset['name'])
            os.makedirs(os.path.dirname(target), exist_ok=True)
            print('  downloading', asset['name'])
            with http_get(asset['browser_download_url'], token) as response, open(target, 'wb') as f:
                shutil.copyfileobj(response, f)
            files.append({**info, 'name': asset['name'], 'size': os.path.getsize(target), 'sha256': sha256_of(target),
                          'url': 'downloads/%s/%s' % (tag, asset['name'])})
        result.append({'tag': tag, 'version': re.sub(r'^[vV]', '', tag), 'prerelease': bool(release.get('prerelease')),
                       'date': release['published_at'][:10], 'notes': release.get('body') or '', 'files': files})
    return result


def releases_from_folder(folder, out):
    """For trying the site on a computer: the installers in <folder> stand in for a release."""
    files = []
    version = '0.0.0'
    for name in sorted(os.listdir(folder)):
        info = describe(name)
        if not info:
            continue
        version = INSTALLER.match(name).group('version')
        target = os.path.join(out, 'downloads', 'V' + version, name)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        shutil.copyfile(os.path.join(folder, name), target)
        files.append({**info, 'name': name, 'size': os.path.getsize(target), 'sha256': sha256_of(target),
                      'url': 'downloads/V%s/%s' % (version, name)})
    if not files:
        return []
    notes = ''
    return [{'tag': 'V' + version, 'version': version, 'prerelease': '-' in version, 'date': datetime.date.today().isoformat(),
             'notes': notes, 'files': files}]


# --------------------------------------------------------------------------------------------------------------- pages

def layout(title, active, body, description, lang='en', page=None):
    template = read(SRC, 'layout.html') if lang == 'en' else read(SRC, lang, 'layout.html')
    nav = ''.join('<a href="%s"%s>%s</a>' % (target, ' aria-current="page"' if target == active else '', esc(label)) for target, label in PAGES[lang])

    # the same page in the other languages: a switch for people, and hreflang for search engines
    def address(l):
        return ROOT_OF[lang] + ('' if l == 'en' else 'de/') + page if lang != l else page

    switch = ''.join('<a class="lang" href="%s" hreflang="%s" lang="%s">%s</a>' % (address(l), l, l, esc(LANGUAGE_NAMES[l])) for l in LANGUAGES if l != lang) if page else ''
    alternates = ''.join('<link rel="alternate" hreflang="%s" href="%s">' % (l, address(l)) for l in LANGUAGES) if page else ''
    return (template.replace('{{title}}', esc(title if title == SITE_NAME else '%s · %s' % (title, SITE_NAME)))
            .replace('{{description}}', esc(description)).replace('{{nav}}', nav).replace('{{content}}', body)
            .replace('{{lang}}', lang).replace('{{root}}', ROOT_OF[lang]).replace('{{switch}}', switch).replace('{{alternates}}', alternates)
            .replace('{{year}}', str(datetime.date.today().year)))


def document_page(title, intro, markdown, converter):
    return '<div class="doc"><h1>%s</h1>%s%s</div>' % (esc(title), '<p class="lead">%s</p>' % esc(intro) if intro else '', converter.convert(markdown))


def release_card(release, latest, lang='en'):
    t = TEXT[lang]
    root = ROOT_OF[lang]
    badges = ''
    if latest:
        badges += '<span class="badge latest">%s</span>' % t['latest']
    if release['prerelease']:
        badges += '<span class="badge early" title="%s">%s</span>' % (esc(t['pre_title']), t['pre'])
    tiles = []
    for f in sorted(release['files'], key=lambda f: f['order']):
        if lang != 'en':
            f = {**f, **dict(zip(('title', 'sub', 'text'), DOWNLOAD_TEXT_DE[f['id']]))}
        tiles.append(
            '<article class="dl" data-os="%s" data-id="%s">'
            '<h3>%s</h3><p class="sub">%s</p><p>%s</p>'
            '<a class="button" href="%s" download>%s <span>%s · %s</span></a>'
            '<details><summary>%s</summary><code class="hash">%s</code></details>'
            '</article>' % (f['os'], f['id'], esc(f['title']), esc(f['sub']), esc(f['text']), esc(root + f['url']), t['download'],
                            esc(os.path.splitext(f['name'])[1] if not f['name'].endswith('.tar.gz') else '.tar.gz'), size_text(f['size']), t['check'], f['sha256']))
    notes = ''
    if release['notes'].strip():
        notes = '<details class="notes"><summary>%s</summary>%s</details>' % (t['notes'], Converter(LINKS, IMAGES).convert(release['notes']))
    return ('<section class="release" data-channel="%s">'
            '<header><h2>Hofwerk %s</h2>%s<span class="date">%s</span></header>'
            '<div class="downloads">%s</div>%s</section>' % (
                'early' if release['prerelease'] else 'stable', esc(release['version']), badges, esc(release['date']), ''.join(tiles), notes))


def download_page(releases, lang='en'):
    t = TEXT[lang]
    if not releases or not any(r['files'] for r in releases):
        return '<div class="doc"><h1>%s</h1><p class="lead">%s</p></div>' % (t['download_page'], t['soon'])

    stable = next((r for r in releases if not r['prerelease']), None)
    early = next((r for r in releases if r['prerelease']), None)
    shown = ''.join(release_card(r, latest=(i == 0), lang=lang) for i, r in enumerate(releases))

    intro = t['intro']
    channel = ''
    if stable and early:
        channel = ('<label class="switch"><input type="checkbox" id="show-early"> ' + t['early'] + '</label>')
    elif early and not stable:
        channel = '<p class="notice">' + t['only_pre'] + '</p>'

    help_html = read(SRC, 'download-help.html') if lang == 'en' else read(SRC, lang, 'download-help.html')
    return '<div class="doc wide"><h1>%s</h1><p class="lead">%s</p>%s<div id="releases">%s</div>%s</div>' % (t['download_page'], esc(intro), channel, shown, help_html)


def home_page(releases, lang='en'):
    t = TEXT[lang]
    root = ROOT_OF[lang]
    body = read(SRC, 'home.html') if lang == 'en' else read(SRC, lang, 'home.html')
    cta = '<a class="button big" href="download.html" data-download-cta>%s</a>' % t['cta']
    if releases and releases[0]['files']:
        newest = releases[0]
        urls = {f['id']: f['url'] for f in newest['files']}
        windows = urls.get('windows-offline') or urls.get('windows-online') or ''
        cta = ('<a class="button big" href="download.html" data-download-cta data-windows="%s" data-linux="%s">%s</a>'
               % (esc(root + windows if windows else ''), esc(root + urls['linux'] if urls.get('linux') else ''), esc(t['cta_version'] % newest['version'])))
        body = body.replace('{{version}}', esc(newest['version']))
    else:
        body = body.replace('{{version}}', '')
    return body.replace('{{cta}}', cta).replace('{{root}}', root)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', default='_site')
    parser.add_argument('--repo', default=os.environ.get('GITHUB_REPOSITORY', 'Kalmai221/Hofwerk'))
    parser.add_argument('--dist', help='use the installers in this folder instead of the releases (to try the site on a computer)')
    args = parser.parse_args()

    out = os.path.abspath(args.out)
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)

    # static files
    shutil.copytree(os.path.join(SRC, 'assets'), os.path.join(out, 'assets'))
    shutil.copyfile(os.path.join(ROOT, 'src', 'Hofwerk.WinUI', 'Assets', 'Hofwerk.png'), os.path.join(out, 'assets', 'hofwerk.png'))
    shutil.copyfile(os.path.join(ROOT, 'src', 'Hofwerk.WinUI', 'Assets', 'Hofwerk.ico'), os.path.join(out, 'favicon.ico'))
    shutil.copyfile(os.path.join(ROOT, 'branding', 'hofwerk-logo.svg'), os.path.join(out, 'assets', 'logo.svg'))
    shutil.copyfile(os.path.join(ROOT, 'docs', 'windows-and-linux.png'), os.path.join(out, 'assets', 'windows-and-linux.png'))
    write(os.path.join(out, '.nojekyll'), '')
    write(os.path.join(out, 'robots.txt'), 'User-agent: *\nAllow: /\n')
    if os.path.exists(os.path.join(HERE, 'CNAME')):
        shutil.copyfile(os.path.join(HERE, 'CNAME'), os.path.join(out, 'CNAME'))

    # releases
    print('Releases...')
    if args.dist:
        releases = releases_from_folder(args.dist, out)
    else:
        try:
            releases = releases_from_github(args.repo, out, os.environ.get('GITHUB_TOKEN'))
        except Exception as ex:   # the site still builds, just without downloads, rather than failing the deploy
            print('  could not read the releases:', ex)
            releases = []
    write(os.path.join(out, 'releases.json'), json.dumps(releases, indent=2))

    # pages, once per language (English at the top of the site, German in /de/)
    changelog = read(ROOT, 'CHANGELOG.md')
    changelog = re.sub(r'(?ms)^## \[Unreleased\]\s*\n+\s*Nothing yet\.\s*\n', '', changelog)    # an empty section is just noise on a website
    changelog_body = '\n'.join(line for line in changelog.split('\n')[1:])
    count = 0
    for lang in LANGUAGES:
        t = TEXT[lang]
        converter = Converter(LINKS, IMAGES if lang == 'en' else IMAGES_DE)
        readme = read(ROOT, SOURCE['readme'][lang])
        # without the title and the line of language links (the site has its own switch)
        body_of = lambda path: '\n'.join(l for l in read(ROOT, path).split('\n')[1:] if not re.match(r'^\[\**English', l))
        pages = {
            'index.html': (SITE_NAME, home_page(releases, lang), t['tag']),
            'download.html': (t['download_page'], download_page(releases, lang), t['dl_desc']),
            'changelog.html': (t['whatsnew'], document_page(t['whatsnew'], t['changelog_lead'], changelog_body, converter), t['changelog_desc']),
            'compare.html': (t['compare'], document_page(t['compare'], '', section(readme, 'Hofwerk and HOF Suite' if lang == 'en' else 'Hofwerk und HOF Suite'), converter), t['compare_desc']),
            'roadmap.html': (t['roadmap'], document_page(t['roadmap'], '', body_of(SOURCE['roadmap'][lang]), converter), t['roadmap_desc']),
            'presets.html': (t['presets'], document_page(t['presets'], '', body_of(SOURCE['presets'][lang]), converter), t['presets_desc']),
        }
        folder = out if lang == 'en' else os.path.join(out, lang)
        for page, (title, body, description) in pages.items():
            write(os.path.join(folder, page), layout(title, page, body, description, lang, page))
        write(os.path.join(folder, '404.html'), layout(t['notfound_title'], '', '<div class="doc"><h1>%s</h1><p class="lead"><a href="index.html">%s</a></p></div>' % (t['notfound'], t['back']), '', lang))
        count += len(pages) + 1

    print('Built %d pages and %d download files into %s' % (count, sum(len(r['files']) for r in releases), out))


if __name__ == '__main__':
    main()
