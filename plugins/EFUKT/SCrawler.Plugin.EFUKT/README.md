# SCrawler.Plugin.EFUKT

GUI-less SCrawler plugin for `efukt.com`.

## What users can enter in SCrawler

- Full listing URL (recommended), for example:
  - `https://efukt.com/`
  - `https://efukt.com/category/teen/`
  - `https://efukt.com/series/`
- Full video URL for single-video download:
  - `https://efukt.com/24459_To_Watch_A_Predator.html`
- Relative site path (plugin converts to absolute URL):
  - `/category/teen/`

## How scraping works

1. Load the user-specified listing page.
2. Extract EFUKT video links in `/{id}_{slug}.html` format.
3. Follow pagination using `next_page` links.
4. Open each video page and extract `<source src="...mp4">` URLs.
5. Download the direct MP4 URL.

## One-file install

1. Build `SCrawler.Plugin.EFUKT` in `Release`.
2. Copy only `SCrawler.Plugin.EFUKT.dll`.
3. Move that single file into SCrawler's `Plugins` folder.

No extra plugin-side dependency files are required when SCrawler already includes `SCrawler.PluginProvider`.
