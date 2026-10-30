# Third-party notices

## Lyricify Lyrics Helper 0.2.0

Copyright 2023 XY Wang (WXRIW). Licensed under Apache License 2.0. The package metadata identifies repository commit `a139e385b032b9abfca4c060627776cf9c3147ad`; the upstream project and license are available at [WXRIW/Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper). The license text is included in `src/OpenOverlay.Infrastructure/LICENSES/Apache-2.0.txt`.

OpenOverlay calls only the package's LRCLIB `Api.Get` adapter. The Spotify web-session lyrics request is implemented separately with a fixed Spotify destination and redirect rejection.

The package declares these transitive dependencies:

- `Newtonsoft.Json` 13.0.3 — MIT; package metadata and source: [NuGet](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3)
- `SharpZipLib` 1.4.2 — MIT; [NuGet](https://www.nuget.org/packages/SharpZipLib/1.4.2)
- `CHTCHSConv` 1.0.0 — Microsoft Visual Studio International Pack component. Its NuGet metadata does not declare an SPDX license expression; check its package terms before redistribution beyond personal use. [NuGet](https://www.nuget.org/packages/CHTCHSConv/1.0.0)

## Microsoft WebView2 SDK 1.0.4258.31

Copyright Microsoft Corporation. The package's redistribution terms are copied to `src/OpenOverlay.Infrastructure/LICENSES/Microsoft.Web.WebView2.txt`. The application requires the Microsoft Edge WebView2 Runtime to be installed on Windows.

## Electron and JavaScript packages

Electron and frontend/build-time dependencies are declared in the root and `web/package.json` manifests. Their package license files and notices are retained in the pnpm installation metadata. Review the resolved dependency licenses before redistributing installer builds.
