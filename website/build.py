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

PAGES = [
    ('index.html', 'Home', None),
    ('download.html', 'Download', None),
    ('changelog.html', "What's new", None),
    ('compare.html', 'Compare', None),
    ('roadmap.html', 'Roadmap', None),
    ('presets.html', 'String presets', None),
]

# Repository files the documents link to, and where they live on the site.
LINKS = {
    'ROADMAP.md': 'roadmap.html',
    'CHANGELOG.md': 'changelog.html',
    'Presets/README.md': 'presets.html',
    'README.md': 'index.html',
    '../../releases': 'download.html',
}
IMAGES = {'docs/windows-and-linux.png': 'assets/windows-and-linux.png'}


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

def layout(title, active, body, description, depth_note=''):
    template = read(SRC, 'layout.html')
    nav = ''.join('<a href="%s"%s>%s</a>' % (page, ' aria-current="page"' if page == active else '', esc(label)) for page, label, _ in PAGES)
    return (template.replace('{{title}}', esc(title if title == SITE_NAME else '%s · %s' % (title, SITE_NAME)))
            .replace('{{description}}', esc(description)).replace('{{nav}}', nav).replace('{{content}}', body)
            .replace('{{year}}', str(datetime.date.today().year)))


def document_page(title, intro, markdown, converter):
    return '<div class="doc"><h1>%s</h1>%s%s</div>' % (esc(title), '<p class="lead">%s</p>' % esc(intro) if intro else '', converter.convert(markdown))


def release_card(release, latest):
    badges = ''
    if latest:
        badges += '<span class="badge latest">Latest</span>'
    if release['prerelease']:
        badges += '<span class="badge early" title="An early version that may have bugs">Pre-release</span>'
    tiles = []
    for f in sorted(release['files'], key=lambda f: f['order']):
        tiles.append(
            '<article class="dl" data-os="%s" data-id="%s">'
            '<h3>%s</h3><p class="sub">%s</p><p>%s</p>'
            '<a class="button" href="%s" download>Download <span>%s · %s</span></a>'
            '<details><summary>Check the file (SHA-256)</summary><code class="hash">%s</code></details>'
            '</article>' % (f['os'], f['id'], esc(f['title']), esc(f['sub']), esc(f['text']), esc(f['url']),
                            esc(os.path.splitext(f['name'])[1] if not f['name'].endswith('.tar.gz') else '.tar.gz'), size_text(f['size']), f['sha256']))
    notes = ''
    if release['notes'].strip():
        notes = '<details class="notes"><summary>What is in this version</summary>%s</details>' % Converter(LINKS, IMAGES).convert(release['notes'])
    return ('<section class="release" data-channel="%s">'
            '<header><h2>Hofwerk %s</h2>%s<span class="date">%s</span></header>'
            '<div class="downloads">%s</div>%s</section>' % (
                'early' if release['prerelease'] else 'stable', esc(release['version']), badges, esc(release['date']), ''.join(tiles), notes))


def download_page(releases):
    if not releases or not any(r['files'] for r in releases):
        body = ('<div class="doc"><h1>Download</h1><p class="lead">The first download is on its way. Come back soon.</p></div>')
        return body

    stable = next((r for r in releases if not r['prerelease']), None)
    early = next((r for r in releases if r['prerelease']), None)
    shown = ''.join(release_card(r, latest=(i == 0)) for i, r in enumerate(releases))

    intro = 'Hofwerk is free. Pick the file for your system, run it, and Hofwerk is ready. It installs just for you and needs no administrator rights.'
    channel = ''
    if stable and early:
        channel = ('<label class="switch"><input type="checkbox" id="show-early"> Also show the newest early version (a pre-release that may have bugs)</label>')
    elif early and not stable:
        channel = '<p class="notice">Every Hofwerk version so far is a <strong>pre-release</strong>: an early version that may have bugs. Hofwerk becomes <strong>V1</strong> when it can do everything HOF Suite does (see the <a href="roadmap.html">roadmap</a>).</p>'

    help_html = read(SRC, 'download-help.html')
    return '<div class="doc wide"><h1>Download</h1><p class="lead">%s</p>%s<div id="releases">%s</div>%s</div>' % (esc(intro), channel, shown, help_html)


def home_page(releases):
    body = read(SRC, 'home.html')
    cta = '<a class="button big" href="download.html" data-download-cta>Download</a>'
    if releases and releases[0]['files']:
        newest = releases[0]
        urls = {f['id']: f['url'] for f in newest['files']}
        windows = urls.get('windows-offline') or urls.get('windows-online') or ''
        cta = ('<a class="button big" href="download.html" data-download-cta data-windows="%s" data-linux="%s">Download Hofwerk %s</a>'
               % (esc(windows), esc(urls.get('linux', '')), esc(newest['version'])))
        body = body.replace('{{version}}', esc(newest['version']))
    else:
        body = body.replace('{{version}}', '')
    return body.replace('{{cta}}', cta)


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

    # pages
    converter = Converter(LINKS, IMAGES)
    changelog = read(ROOT, 'CHANGELOG.md')
    changelog = re.sub(r'(?ms)^## \[Unreleased\]\s*\n+\s*Nothing yet\.\s*\n', '', changelog)    # an empty section is just noise on a website
    changelog_body = '\n'.join(line for line in changelog.split('\n')[1:])
    readme = read(ROOT, 'README.md')

    pages = {
        'index.html': (SITE_NAME, home_page(releases), TAGLINE + '. Edit destinations, bus stops and routes for Windows and Linux.'),
        'download.html': ('Download', download_page(releases), 'Download Hofwerk for Windows or Linux.'),
        'changelog.html': ("What's new", document_page("What's new", 'What changed in each version of Hofwerk.', changelog_body, converter), 'What changed in each version of Hofwerk.'),
        'compare.html': ('Hofwerk and HOF Suite', document_page('Hofwerk and HOF Suite', '', section(readme, 'Hofwerk and HOF Suite'), converter), 'How Hofwerk compares with the original HOF Suite.'),
        'roadmap.html': ('Roadmap', document_page('Roadmap', '', '\n'.join(read(ROOT, 'ROADMAP.md').split('\n')[1:]), converter), 'What is done and what is left before Hofwerk 1.0.'),
        'presets.html': ('String presets', document_page('String presets', '', '\n'.join(read(ROOT, 'Presets', 'README.md').split('\n')[1:]), converter), 'How to name the string slots of your bus, and how to share your names.'),
    }
    for page, (title, body, description) in pages.items():
        write(os.path.join(out, page), layout(title, page, body, description))
    write(os.path.join(out, '404.html'), layout('Not found', '', '<div class="doc"><h1>That page is not here</h1><p class="lead"><a href="index.html">Back to the home page</a></p></div>', ''))

    print('Built %d pages and %d download files into %s' % (len(pages) + 1, sum(len(r['files']) for r in releases), out))


if __name__ == '__main__':
    main()
