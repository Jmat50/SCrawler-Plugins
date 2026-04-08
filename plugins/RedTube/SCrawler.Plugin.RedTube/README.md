# SCrawler.Plugin.RedTube

GUI-less SCrawler plugin for `redtube.com`.

## What users can enter in SCrawler

- Full listing URL (recommended), for example:
  - `https://www.redtube.com/newest`
  - `https://www.redtube.com/top?period=weekly`
  - `https://www.redtube.com/redtube/anal`
  - `https://www.redtube.com/pornstar/steve+french`
  - `https://www.redtube.com/channel/top-rated`
- Full single-video URL:
  - `https://www.redtube.com/220763661`
- Relative site path (plugin converts to absolute URL):
  - `/newest`

The plugin rejects unsupported member/profile-style paths such as `/users/.../videos`, because those routes currently do not behave like public video listings.

The plugin follows `rel="next"` pagination automatically.

## How scraping works

1. Load the user-specified listing page.
2. Extract video links from numeric paths such as `/{videoId}`.
3. Open each video page and locate the MP4 endpoint (`/media/mp4?s=...`).
4. Request the MP4 JSON and select the default/best quality source.
5. Download the direct MP4 URL.

## One-file install

1. Build `SCrawler.Plugin.RedTube` in `Release`.
2. Copy only `SCrawler.Plugin.RedTube.dll`.
3. Move that single file into SCrawler's `Plugins` folder.

No extra plugin-side dependency files are required when SCrawler already includes `SCrawler.PluginProvider`.
