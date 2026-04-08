# SCrawler.Plugin.Motherless

GUI-less SCrawler plugin for `motherless.com`.

## What users can enter in SCrawler

- Full listing URL (recommended), for example:
  - `https://motherless.com/videos/recent`
  - `https://motherless.com/videos/popular`
  - `https://motherless.com/porn/anal/videos`
- Full video URL for single-video download:
  - `https://motherless.com/F8BAA3B`
- Relative site path (plugin converts to absolute URL):
  - `/videos/recent`

## How scraping works

1. Load the user-specified listing page.
2. Extract Motherless video links from media codenames.
3. Follow pagination with `rel="next"` links.
4. Open each video page and extract direct MP4 source URLs.
5. Select best available quality and download the MP4.

## One-file install

1. Build `SCrawler.Plugin.Motherless` in `Release`.
2. Copy only `SCrawler.Plugin.Motherless.dll`.
3. Move that single file into SCrawler's `Plugins` folder.

No extra plugin-side dependency files are required when SCrawler already includes `SCrawler.PluginProvider`.
