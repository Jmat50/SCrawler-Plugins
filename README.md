# SCrawler-Plugins

A fully Windows-focused SCrawler plugin port, with the packaging and installer experience built in Python for native Windows use.

This repository is organized as a standalone workspace for building, packaging, and publishing SCrawler-compatible plugin DLLs while keeping the workflow centered on Windows.

Use the Windows Installer to install all plugins via GUI at \SCrawler-plugins\GUI-installer.

## Included

- `SCrawler-Plugins.sln` for the plugin workspace solution
- `SCrawler.PluginProvider` for the shared plugin contracts
- `plugins\*` for site-specific plugin projects
- `GUI-installer` for the Python-based Windows installer and packaging utility
- `build-release.ps1` for release DLL builds

## Build Output

Each plugin builds to a single DLL that can be copied into SCrawler's runtime `Plugins` folder.

- `plugins\Coomer\SCrawler.Plugin.Coomer\bin\Release\SCrawler.Plugin.Coomer.dll`
- `plugins\YouPorn\SCrawler.Plugin.YouPorn\bin\Release\SCrawler.Plugin.YouPorn.dll`
- `plugins\XNXX\SCrawler.Plugin.XNXX\bin\Release\SCrawler.Plugin.XNXX.dll`
- `plugins\Motherless\SCrawler.Plugin.Motherless\bin\Release\SCrawler.Plugin.Motherless.dll`
- `plugins\EFUKT\SCrawler.Plugin.EFUKT\bin\Release\SCrawler.Plugin.EFUKT.dll`
- `plugins\RedTube\SCrawler.Plugin.RedTube\bin\Release\SCrawler.Plugin.RedTube.dll`
- `plugins\Imgur\SCrawler.Plugin.Imgur\bin\Release\SCrawler.Plugin.Imgur.dll`
- `plugins\Mastodon\SCrawler.Plugin.Mastodon\bin\Release\SCrawler.Plugin.Mastodon.dll`
- `plugins\VK\SCrawler.Plugin.VK\bin\Release\SCrawler.Plugin.VK.dll`
- `plugins\Tumblr\SCrawler.Plugin.Tumblr\bin\Release\SCrawler.Plugin.Tumblr.dll`
- `plugins\DeviantArt\SCrawler.Plugin.DeviantArt\bin\Release\SCrawler.Plugin.DeviantArt.dll`

## Notes

- This repository is intentionally Windows-first, and its installer workflow is built in Python for Windows users.
- Plugin assembly names stay on the `SCrawler.Plugin.*` pattern so they remain compatible with SCrawler's loader.
- Generated build output, release bundles, and the local upstream reference mirror are not intended to be committed as source.
