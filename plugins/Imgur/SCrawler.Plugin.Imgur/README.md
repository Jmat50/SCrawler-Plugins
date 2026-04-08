# SCrawler.Plugin.Imgur

GUI-less SCrawler plugin for `imgur.com`.

## What users can enter in SCrawler

- Full listing URL (recommended), for example:
  - `https://imgur.com/`
  - `https://imgur.com/gallery/hot`
  - `https://imgur.com/t/funny`
- Full post URL:
  - `https://imgur.com/gallery/over-oc-BBkBsrE`
  - `https://imgur.com/a/XO7rBtL`
  - `https://imgur.com/BBaW007`
- Direct media URL:
  - `https://i.imgur.com/BBaW007.png`
- Relative site path:
  - `/gallery/hot`

## How scraping works

1. Load the user-specified listing/post URL.
2. Extract post links (`/gallery/...`, `/a/...`, `/t/.../{id}`, `/{id}`).
3. For each post, extract direct `i.imgur.com` media URLs.
4. Normalize thumbnail/preview URLs to downloadable media URLs.
5. Download image/video files directly from `i.imgur.com`.

If Imgur blocks direct requests (403/429), the plugin automatically retries page parsing via an HTML mirror fallback.

## One-file install

1. Build `SCrawler.Plugin.Imgur` in `Release`.
2. Copy only `SCrawler.Plugin.Imgur.dll`.
3. Move that single file into SCrawler's `Plugins` folder.

No extra plugin-side dependency files are required when SCrawler already includes `SCrawler.PluginProvider`.
