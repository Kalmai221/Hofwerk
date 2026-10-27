# Logo

`hofwerk-logo.svg` is the logo: the front of a bus with its destination sign (the thing Hofwerk edits), on a blue tile.
`hofwerk-logo-small.svg` is the same picture with fewer, bolder parts, used for the 16 to 32 pixel sizes so it stays readable.

Every icon in the project is made from these two files:

```powershell
pip install pillow
python branding/make-icons.py
```

It needs Pillow and Microsoft Edge (or Chrome), which draws the SVG, and writes the Windows and Linux app icons, the installer icon and
`preview.png` (not kept in git) with every size side by side. The website uses `hofwerk-logo.svg` directly.

Colours: tile `#22B0FF` to `#0041AD`, bus `#FFFFFF`, sign `#0B2D70`, amber `#FFB300`.
