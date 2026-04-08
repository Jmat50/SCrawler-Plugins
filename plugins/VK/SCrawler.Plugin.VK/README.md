# SCrawler.Plugin.VK

GUI-less SCrawler plugin for `vk.com`.

## What users can enter in SCrawler

- Profile/community page URL:
  - `https://vk.com/id1`
  - `https://vk.com/public1`
  - `https://vk.com/club1`
- Post/media page URL:
  - `https://vk.com/wall-1_1`
  - `https://vk.com/photo-1_1`
  - `https://vk.com/video-1_1`
- Short key / path:
  - `id1`
  - `public1`
  - `/wall-1_1`
- Direct media URL from VK CDN/userapi.

## How scraping works

1. Loads the page URL you provide.
2. Extracts direct media URLs (images/videos/audio) from page source.
3. Extracts VK post links (`wall/photo/video/...`) and parses those pages too.
4. Downloads all discovered direct media links.

## Notes

- VK can serve different page versions depending on anti-bot checks and region.
- Private, age-restricted, or login-gated media may not be downloadable without account access.

## One-file install

1. Build `SCrawler.Plugin.VK` in `Release`.
2. Copy only `SCrawler.Plugin.VK.dll`.
3. Put that single DLL into SCrawler's `Plugins` folder.

No extra plugin-side dependency files are required when SCrawler already includes `SCrawler.PluginProvider`.
