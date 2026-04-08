# SCrawler.Plugin.YouPorn

GUI-less SCrawler plugin for `youporn.com`.

## What users can enter in SCrawler

- Full listing URL (recommended), for example:
  - `https://www.youporn.com/channel/straight/`
  - `https://www.youporn.com/pornstar/lana-rhoades/`
  - `https://www.youporn.com/category/squirting/`
  - `https://www.youporn.com/porntags/tits/`
  - `https://www.youporn.com/browse/time/`
  - `https://www.youporn.com/recommended/`
- Full watch URL for single-video download:
  - `https://www.youporn.com/watch/123456/example-title/`
- Relative site path (plugin converts to absolute URL):
  - `/channel/straight/`

The plugin intentionally rejects site pages that currently expose no public watch links, such as broad index pages like `/categories/` or profile-style `/user/...` routes that return empty video data.

The plugin follows `rel="next"` pagination automatically.

## How scraping works

1. Load the user-specified listing page.
2. Extract watch links from `/watch/{id}/...` URLs.
3. Open each watch page and find the MP4 endpoint (`/media/mp4/?s=...`).
4. Request the MP4 JSON and select the default/best quality source.
5. Download the direct MP4 URL.

## One-file install

1. Build `SCrawler.Plugin.YouPorn` in `Release`.
2. Copy only `SCrawler.Plugin.YouPorn.dll`.
3. Move that single file into SCrawler's `Plugins` folder.

No extra plugin-side dependency files are required when SCrawler already includes `SCrawler.PluginProvider`.
