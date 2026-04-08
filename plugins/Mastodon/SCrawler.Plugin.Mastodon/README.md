# SCrawler.Plugin.Mastodon

GUI-less SCrawler plugin for Mastodon instances, with default support for `mastodon.social`.

## What users can enter in SCrawler

- Profile URL:
  - `https://mastodon.social/@username`
  - `https://mastodon.social/users/username`
- Status URL:
  - `https://mastodon.social/@username/114319569356170798`
  - `https://mastodon.social/web/statuses/114319569356170798`
- Account shorthand:
  - `@username`
  - `username@mastodon.social`
- Direct media URL ending in a media extension.

## How scraping works

1. Resolve input as profile, status, or direct media URL.
2. For profiles, use Mastodon public API account lookup, then page through account statuses with `only_media=true`.
3. For statuses, fetch the status by id and extract `media_attachments`.
4. Download media URLs directly.

## One-file install

1. Build `SCrawler.Plugin.Mastodon` in `Release`.
2. Copy only `SCrawler.Plugin.Mastodon.dll`.
3. Move that one file into SCrawler's `Plugins` folder.

No extra plugin-side dependency files are required when SCrawler already includes `SCrawler.PluginProvider`.
