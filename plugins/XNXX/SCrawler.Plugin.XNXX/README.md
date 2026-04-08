# SCrawler.Plugin.XNXX

GUI-less SCrawler plugin for `xnxx.com`.

## What users can enter in SCrawler

- Full listing URL (recommended), for example:
  - `https://www.xnxx.com/search/test/`
  - `https://www.xnxx.com/new/`
  - `https://www.xnxx.com/best/`
- Full video URL for single-video download:
  - `https://www.xnxx.com/video-abcdef/example-title`
- Relative site path (plugin converts to absolute URL):
  - `/search/test/`

## How scraping works

1. Load the user-specified listing page.
2. Extract video links from `/video-...` URLs.
3. Follow pagination using `rel="next"` or page links with `class="next"`.
4. Open each video page and extract direct MP4 URLs.
5. Select the best quality MP4 and download it.

## One-file install

1. Build `SCrawler.Plugin.XNXX` in `Release`.
2. Copy only `SCrawler.Plugin.XNXX.dll`.
3. Move that single file into SCrawler's `Plugins` folder.

No extra plugin-side dependency files are required when SCrawler already includes `SCrawler.PluginProvider`.
