# SCrawler.Plugin.Coomer

GUI-less SCrawler plugin for `coomer.st`.

## Supported URL format

- `https://coomer.st/{service}/user/{creator_id}`
- Example: `https://coomer.st/onlyfans/user/sukoshicosplay`

## What it scrapes

- Creator profile check: `/api/v1/{service}/user/{creator_id}/profile`
- Creator posts (paged): `/api/v1/{service}/user/{creator_id}/posts?o={offset}`
- Media from both `file` and `attachments`

## Important API header

Coomer currently requires this for scraper/API calls:

- `Accept: text/css`

The plugin sets that header automatically in `UserData.vb`.

## One-file install for end users

1. Build `SCrawler.Plugin.Coomer` in `Release`.
2. Take only `SCrawler.Plugin.Coomer.dll`.
3. Close SCrawler.
4. Move that single DLL into SCrawler's `Plugins` folder.
5. Start SCrawler.

No extra dependency files are required in the plugin folder when SCrawler already includes `SCrawler.PluginProvider`.
