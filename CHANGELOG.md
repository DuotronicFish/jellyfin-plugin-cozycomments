# Changelog

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
