# Changelog

## 2.0.1.0

- Fixed: on Jellyfin 12.0 to 12.3 the plugin did not load ("Could not load file or assembly 'Microsoft.Data.Sqlite,
  Version=10.0.14.0'", status *Malfunctioned*). 2.0.0.0 asked for the SQLite library version that ships with Jellyfin
  12.4, and older 12.x servers ship an earlier one. It now asks for 10.0.0, which every 12.x server has.
- No other changes. Comments, settings and backups are kept when updating.
- Tested on Jellyfin 12.0, 12.2, 12.3 and 12.4 with File Transformation 3.0.3.0 (the build for each version),
  including an update from 2.0.0.0 on 12.4.

## 2.0.0.0

- **Requires Jellyfin 12.x** (built for .NET 10 against Jellyfin 12.0, `targetAbi` 12.0.0.0) and the Jellyfin 12 build of
  File Transformation. On Jellyfin 10.11.x keep using **1.2.1.0**; the plugin repository keeps offering it to 10.11 servers.
- No changes to comments, settings or the API. Upgrading Jellyfin from 10.11 to 12 keeps all comments (the database
  lives in the plugin's own folder and is not touched by Jellyfin's upgrade). The `/JellyfinComments` address and the
  data carry-over from the old `Jellyfin.Plugin.Comments` folder are still there.
- Fixed (Jellyfin 12 web app): the *My Media* comments card no longer causes a script error on every library-scan
  progress message (jellyfin-web 12 starts socket subscriptions inside cloned cards).
- Tested on Jellyfin 12.4 with File Transformation 3.0.3.0 (12.4 build), in both the Modern and the Legacy layout.

## 1.2.1.0

- Fixed: after upgrading from Lagtime Comments / Jellyfin Comments, browser tabs that were open during the upgrade
  showed "Could not load comments: Request failed (404)" on the comments page until reloaded. The plugin now also
  answers on the old `/JellyfinComments` address, so those tabs keep working.
- Fixed: the comments page could close by itself right after opening when the plugin's settings changed (for
  example after an upgrade or when an admin saved the settings). It now stays open unless the page is turned off.
- Errors now say what went wrong and how to fix it, with **Try again** / **Reload page** buttons, instead of
  "Request failed (404)".
- Open pages notice when a newer version is installed on the server and offer a **Reload** button.
- The comments page being turned off now answers 403 with an explanation instead of 404.

## 1.2.0.0

- Renamed to **Cozy Comments** (assembly `Jellyfin.Plugin.CozyComments`, API under `/CozyComments`). The plugin GUID is
  unchanged, so this installs as an upgrade.
- On first start, comments, backups, the card image and settings are copied over from the old
  `Jellyfin.Plugin.Comments` folder and settings file. The old ones are left in place; delete them once you're happy.

## 1.1.0.0

First public release.

- Comments on movies, series, episodes and home videos, placed under the item's details
- Optional 1–5 star ratings, spoiler blur, one-level replies, edit and delete
- Comments page (newest comments with posters), opened from a card in *My Media*
- Every feature, colour and text can be changed in the Dashboard or with `comments.conf` + `scripts/configure.py`
- Card image upload
- Per-user rate limits, Jellyfin library access checks on every request
- Daily SQLite backups with rotation
