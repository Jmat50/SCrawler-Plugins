# SCrawler.Plugin.DeviantArt

GUI-less SCrawler plugin for `www.deviantart.com`.

## What users can enter in SCrawler

- Profile/page URL:
  - `https://www.deviantart.com/username`
  - `https://www.deviantart.com/username/gallery`
  - `https://www.deviantart.com/tag/sci-fi`
- Deviation URL:
  - `https://www.deviantart.com/username/art/example-title-123456789`
  - `https://www.deviantart.com/deviation/123456789`
- Short key or path:
  - `username`
  - `/username/gallery`
  - `/deviation/123456789`
- Direct media URL:
  - `https://images-wixmp...wixmp.com/...jpg`
  - `https://...deviantart.net/...png`

## How scraping works

1. Loads the page URL supplied in SCrawler.
2. Extracts direct media links from source (image/video/audio).
3. Follows discovered deviation links and next-page links.
4. Downloads all discovered direct media links.

## One-file install

1. Build `SCrawler.Plugin.DeviantArt` in `Release`.
2. Copy only `SCrawler.Plugin.DeviantArt.dll`.
3. Put that DLL into SCrawler's `Plugins` folder.
