# The Hofwerk website

Everything people need is on the website: what Hofwerk is, the **downloads**, what is new, how it compares with HOF Suite, the roadmap
and the string presets guide. The downloads are copied onto the site itself, so nobody has to visit the code host to get Hofwerk.

It is plain HTML, CSS and a few lines of JavaScript, made by `build.py` (Python 3, nothing to install).

| Page | Where it comes from |
| --- | --- |
| Home | `website/src/home.html` |
| Download | the newest release (and the newest pre-release) of the repository, plus `website/src/download-help.html` |
| What's new | `CHANGELOG.md` |
| Compare | the "Hofwerk and HOF Suite" part of `README.md` |
| Roadmap | `ROADMAP.md` |
| String presets | `Presets/README.md` |

So the changelog, the roadmap and the comparison are edited in one place only (the Markdown files), and the site follows.

## Try it on your computer

```powershell
python website/build.py --out _site --dist dist     # pretends the installers in dist/ are the newest release
python -m http.server 8000 --directory _site        # then open http://localhost:8000
```

Without `--dist` it reads the real releases (set `GITHUB_TOKEN` to avoid the anonymous rate limit).

## Publishing

`.github/workflows/website.yml` builds and publishes the site with GitHub Pages:

- when anything it is made from changes on `main`,
- after the Release workflow finishes, so the Download page gets the new installers,
- by hand (Actions tab, Website, Run workflow).

**Once**, in the repository settings: *Pages → Build and deployment → Source: GitHub Actions*.

### Your own address

The site works at any address. To use your own domain (for example `hofwerk.example`), put the domain on one line in
`website/CNAME`, point the domain's DNS at GitHub Pages, and set the same domain under *Settings → Pages*. Then change the
website address in `HofHeader.Url` (`src/Hofwerk.Core/Model/HofHeader.cs`) and `AppInfo.Url`
(`src/Hofwerk.Installer.Engine/Models.cs`); that is the link in every file Hofwerk makes and in Windows' *Installed apps*.
The old `github.io` address then forwards to the new one.

### Size

A release is about 110 MB of installers. The site keeps the newest release and the newest pre-release only, well inside the
1 GB limit of GitHub Pages.

## What still points to GitHub

Only what has to: the "Report a bug" link (new issues are opened there) and the update check inside the app, which reads the release
list. Nothing on the site links to the code or to a profile.
