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

# The site is built once per language: English at the top, every other language in a folder named by its code (/de/, /pt-BR/). A
# language is a folder website/src/<code>/ with a strings.json (its menu, its page texts, the names of the downloads) and, for the
# parts that are written out in full, layout.html, home.html and download-help.html. English is the one in website/src itself.
# Anything a language does not have yet falls back to English, so a half-finished translation never breaks the site. The documents
# come from README.<code>.md, ROADMAP.<code>.md and Presets/README.<code>.md when those exist.
DOC_SOURCES = {'readme': 'README', 'roadmap': 'ROADMAP', 'presets': 'Presets/README'}


def load_languages():
    """{code: strings} for English and every folder of website/src that has a strings.json; English first, the others by code."""
    languages = {}
    with open(os.path.join(SRC, 'strings.json'), encoding='utf-8') as f:
        languages['en'] = json.load(f)
    for entry in sorted(os.listdir(SRC)):
        path = os.path.join(SRC, entry, 'strings.json')
        if os.path.isfile(path):
            with open(path, encoding='utf-8') as f:
                strings = json.load(f)
            # a text the language does not have yet is the English one
            strings['text'] = {**languages['en']['text'], **strings.get('text', {})}
            strings.setdefault('nav', languages['en']['nav'])
            strings.setdefault('downloads', languages['en']['downloads'])
            strings.setdefault('compare_section', languages['en']['compare_section'])
            languages[entry] = strings
    return languages


LANGUAGES = load_languages()
CODES = list(LANGUAGES)


def root_of(code):
    """How a page of that language gets to the files at the top of the site."""
    return '' if code == 'en' else '../'


def folder_of(code):
    return '' if code == 'en' else code + '/'


def fragment(code, name):
    """A layout, home page or download help of a language; the English one when the language has not written its own."""
    own = os.path.join(SRC, code, name)
    if code != 'en' and os.path.isfile(own):
        return read(own)
    if code != 'en':
        print('  %s: no %s of its own, using the English one' % (code, name))
    return read(SRC, name)


def document(code, kind):
    """The repository file a document page of a language comes from, falling back to English: translations/<code>/README.md ..."""
    english, translated = {'readme': ('README.md', 'README.md'), 'roadmap': ('ROADMAP.md', 'ROADMAP.md'), 'presets': ('Presets/README.md', 'Presets.md')}[kind]
    if code != 'en':
        own = os.path.join(ROOT, 'translations', code, translated)
        if os.path.isfile(own):
            return own
        print('  %s: no translations/%s/%s, using the English one' % (code, code, translated))
    return os.path.join(ROOT, english)


# Repository files the documents link to, and where they live on the site.
LINKS = {
    'ROADMAP.md': 'roadmap.html',
    'CHANGELOG.md': 'changelog.html',
    'Presets/README.md': 'presets.html',
    'README.md': 'index.html',
    '../../releases': 'download.html',
}
IMAGES = {'docs/windows-and-linux.png': 'assets/windows-and-linux.png'}


def links_for(code):
    """
    The links in the documents of a language. A translation sits in translations/<code>/, so it names the same pages by other paths:
    the changelog is ../../CHANGELOG.md, and its own roadmap and presets guide are next to it.
    """
    links = dict(LINKS)
    links.update({'Presets.md': 'presets.html', '../../README.md': 'index.html', '../../ROADMAP.md': 'roadmap.html', '../../CHANGELOG.md': 'changelog.html',
                  '../../Presets/README.md': 'presets.html', '../../../../releases': 'download.html'})
    return links


def without_selector(markdown):
    """A document without its language selector (the site has its own) and without its title."""
    text = re.sub(r'<!-- languages -->.*?<!-- /languages -->\n*', '', markdown, flags=re.S)
    lines = text.split('\n')
    return '\n'.join(lines[1:] if text.startswith('# ') else lines)


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
    template = fragment(lang, 'layout.html')
    nav = ''.join('<a href="%s"%s>%s</a>' % (target, ' aria-current="page"' if target == active else '', esc(label)) for target, label in LANGUAGES[lang]['nav'])

    # the same page in the other languages: a switch for people, and hreflang for search engines
    def address(l):
        return root_of(lang) + folder_of(l) + page if lang != l else page

    switch = ''.join('<a class="lang" href="%s" hreflang="%s" lang="%s">%s</a>' % (address(l), l, l, esc(LANGUAGES[l]['name'])) for l in CODES if l != lang) if page else ''
    alternates = ''.join('<link rel="alternate" hreflang="%s" href="%s">' % (l, address(l)) for l in CODES) if page else ''
    return (template.replace('{{title}}', esc(title if title == SITE_NAME else '%s · %s' % (title, SITE_NAME)))
            .replace('{{description}}', esc(description)).replace('{{nav}}', nav).replace('{{content}}', body)
            .replace('{{lang}}', lang).replace('{{root}}', root_of(lang)).replace('{{switch}}', switch).replace('{{alternates}}', alternates)
            .replace('{{year}}', str(datetime.date.today().year)))


def document_page(title, intro, markdown, converter):
    return '<div class="doc"><h1>%s</h1>%s%s</div>' % (esc(title), '<p class="lead">%s</p>' % esc(intro) if intro else '', converter.convert(markdown))


def release_card(release, latest, lang='en'):
    t = LANGUAGES[lang]['text']
    root = root_of(lang)
    badges = ''
    if latest:
        badges += '<span class="badge latest">%s</span>' % t['latest']
    if release['prerelease']:
        badges += '<span class="badge early" title="%s">%s</span>' % (esc(t['pre_title']), t['pre'])
    tiles = []
    for f in sorted(release['files'], key=lambda f: f['order']):
        if lang != 'en':
            f = {**f, **dict(zip(('title', 'sub', 'text'), LANGUAGES[lang]['downloads'][f['id']]))}
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
    t = LANGUAGES[lang]['text']
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

    help_html = fragment(lang, 'download-help.html')
    return '<div class="doc wide"><h1>%s</h1><p class="lead">%s</p>%s<div id="releases">%s</div>%s</div>' % (t['download_page'], esc(intro), channel, shown, help_html)


def home_page(releases, lang='en'):
    t = LANGUAGES[lang]['text']
    root = root_of(lang)
    body = fragment(lang, 'home.html')
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
    for lang in CODES:
        print('Language', lang)
        t = LANGUAGES[lang]['text']
        converter = Converter(links_for(lang), IMAGES if lang == 'en' else {**{k: root_of(lang) + v for k, v in IMAGES.items()}, **{'../../' + k: root_of(lang) + v for k, v in IMAGES.items()}})
        readme = read(document(lang, 'readme'))
        body_of = lambda path: without_selector(read(path))   # without the title and the language selector (the site has its own switch)
        pages = {
            'index.html': (SITE_NAME, home_page(releases, lang), t['tag']),
            'download.html': (t['download_page'], download_page(releases, lang), t['dl_desc']),
            'changelog.html': (t['whatsnew'], document_page(t['whatsnew'], t['changelog_lead'], changelog_body, converter), t['changelog_desc']),
            'compare.html': (t['compare'], document_page(t['compare'], '', section(readme, LANGUAGES[lang]['compare_section']), converter), t['compare_desc']),
            'roadmap.html': (t['roadmap'], document_page(t['roadmap'], '', body_of(document(lang, 'roadmap')), converter), t['roadmap_desc']),
            'presets.html': (t['presets'], document_page(t['presets'], '', body_of(document(lang, 'presets')), converter), t['presets_desc']),
        }
        folder = os.path.join(out, folder_of(lang)) if lang != 'en' else out
        for page, (title, body, description) in pages.items():
            write(os.path.join(folder, page), layout(title, page, body, description, lang, page))
        write(os.path.join(folder, '404.html'), layout(t['notfound_title'], '', '<div class="doc"><h1>%s</h1><p class="lead"><a href="index.html">%s</a></p></div>' % (t['notfound'], t['back']), '', lang))
        count += len(pages) + 1

    print('Built %d pages and %d download files into %s' % (count, sum(len(r['files']) for r in releases), out))


if __name__ == '__main__':
    main()
