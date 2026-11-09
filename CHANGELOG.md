# Changelog

## 1.2.2.0

- Fixed: on Jellyfin 10.11.0 to 10.11.4 the plugin did not load ("Could not load file or assembly 'Microsoft.Data.Sqlite,
  Version=9.0.11.0'", status *Malfunctioned*). It asked for the SQLite library version that ships with Jellyfin 10.11.5
  and later. It now asks for 9.0.0, which every 10.11.x server has.
- No other changes. Comments, settings and backups are kept when updating.
- Tested on Jellyfin 10.11.4 (File Transformation 2.5.5.0) and 10.11.11 (File Transformation 3.0.3.0).

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
