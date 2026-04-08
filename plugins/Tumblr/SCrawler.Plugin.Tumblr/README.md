# SCrawler.Plugin.Tumblr

GUI-less SCrawler plugin for `tumblr.com`.

## What users can enter in SCrawler

- Blog/page URL:
  - `https://staff.tumblr.com`
  - `https://www.tumblr.com/staff`
  - `https://www.tumblr.com/tagged/art`
- Post URL:
  - `https://staff.tumblr.com/post/123456789012/example-post`
  - `https://www.tumblr.com/blog/view/staff/123456789012`
- Short key or path:
  - `staff`
  - `/tagged/art`
- Direct Tumblr media URL:
  - `https://64.media.tumblr.com/.../tumblr_xxx_1280.jpg`
  - `https://va.media.tumblr.com/.../tumblr_xxx.mp4`

## How scraping works

1. Loads the page URL supplied in SCrawler.
2. Extracts direct Tumblr media links (image/video/audio) from source.
3. Follows discovered Tumblr post links and next-page links.
4. Downloads all discovered direct media links.

## One-file install

1. Build `SCrawler.Plugin.Tumblr` in `Release`.
2. Copy only `SCrawler.Plugin.Tumblr.dll`.
3. Put that DLL into SCrawler's `Plugins` folder.
