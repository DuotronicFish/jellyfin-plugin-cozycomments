# Configuration reference

There are two ways to change the settings. Both edit the same settings on the server.

- **Dashboard → Plugins → Cozy Comments**: a form with every option, plus the card image upload.
- **`comments.conf` + `scripts/configure.py`**: a text file you push with one command (see the README).

Changes apply without restarting Jellyfin. The web app reads the settings when it loads and then
at most once a minute, so people see changes when they refresh or reopen Jellyfin.

The server enforces every feature switch, not just the web app: with replies turned off, for example,
the API refuses new replies too.

---

## Comments page

The comments page is a full-screen list of the newest comments from every item the viewer can access.
It opens from the card on the home screen.

| Dashboard field | `comments.conf` | Type | Default | Notes |
|---|---|---|---|---|
| Enable the comments page | `[page] enabled` | yes/no | yes | The master switch. Off removes the page and its home screen card, and the page's API returns 404. Comments on movie and show pages keep working. |
| Page name | `[page] name` | text (≤ 80) | `Recent Comments` | Shown as the page title. Also the card label unless *Card label* is set. |
| Card image | `[page] image` | file path / `none` | none | JPEG, PNG, WebP or GIF, at most 5 MB. A 16:9 image fits Jellyfin's library tiles exactly. Without an image the card shows a speech-bubble icon. A relative path is relative to the `comments.conf` file. |
| Page background colour | `[page] background_color` | colour | `#101010` | Also used as the tint behind the spoiler text. |
| Show the poster… | `[page] show_posters` | yes/no | yes | Each row shows the item's poster on the right. Episodes show their series' poster. |
| Include spoiler comments | `[page] show_spoilers` | yes/no | no | They stay blurred until clicked, same as on the item page. |
| Include replies | `[page] show_replies` | yes/no | no | Replies show as "*name* replied on *title*". |
| Comments per "Load more" | `[page] page_size` | 1–50 | 20 | |

## Home screen card

The card is a copy of your library tiles, so it matches your theme. It only appears while the comments page is enabled.

| Dashboard field | `comments.conf` | Type | Default | Notes |
|---|---|---|---|---|
| Card label | `[home] card_label` | text | *(page name)* | |
| Place the card after this library | `[home] card_after_library` | text | *(last)* | The library's name as shown in Jellyfin, not case-sensitive. If no library has that name, the card goes last. |
| Rename "My Media" | `[home] rename_my_media_to` | text | *(off)* | Optional. Renames the heading above the library tiles. Works in any UI language. |

## Colours

Colours can be any CSS colour: `#ffcc00`, `#fc0`, `#ffcc0080`, `rgb(255 204 0)`, `hsl(48 100% 50%)` or a
name such as `gold`. Text colours follow your Jellyfin theme automatically.

| Dashboard field | `comments.conf` | Default | Used for |
|---|---|---|---|
| Accent colour | `[colors] accent_color` | `#00a4dc` (Jellyfin blue) | Comment button, links, "show more", "View replies", spoiler border |
| Star colour | `[colors] star_color` | `#f5c518` | Filled stars, in the picker and on comments |
| Page background colour | `[page] background_color` | `#101010` | The comments page |

## Features

| Dashboard field | `comments.conf` | Default | When turned off |
|---|---|---|---|
| Star ratings | `[features] star_ratings` | yes | No star picker, and existing stars are hidden. They're kept in the database and come back if you turn this on again. |
| Spoilers | `[features] spoilers` | yes | No "Spoiler" checkbox. Existing spoiler comments stay blurred. |
| Replies | `[features] replies` | yes | No "Reply" button, and the API refuses replies. Existing replies are still shown. |
| Users can edit… | `[features] editing` | yes | No "Edit" button, and the API refuses edits. |
| Users can delete… | `[features] deleting` | yes | Users can't delete their own comments. Administrators can always delete any comment. |
| Maximum characters | `[features] max_comment_length` | 2000 | 1–10000. Applies to new comments and edits. |
| Preview length (words) | `[features] preview_word_limit` | 30 | Comments with more words show the first N, then "…" and the "show more" button. 0 turns this off. The full text is always stored. |
| "Show more" text | `[features] show_more_text` | `show more` | |
| Collapsible sections | `[features] collapse_detail_sections` | no | When on: Cast & Crew, Guest Stars, Special Features, Scenes, Additional Parts and Music Videos get a +/− button and start collapsed on every visit. Seasons, episodes and Next Up are never collapsed. |

## Where comments are allowed

| `comments.conf` | Default | Items |
|---|---|---|
| `[allow_comments_on] movies` | yes | Movies |
| `[allow_comments_on] series` | yes | TV series (the show itself) |
| `[allow_comments_on] episodes` | yes | TV episodes |
| `[allow_comments_on] videos` | yes | Plain videos (*Home videos and photos* libraries) |

Comments that already exist stay in the database when you turn a type off. They're just not shown.

## Texts

| Dashboard field | `comments.conf` | Default |
|---|---|---|
| Heading on movie / show pages | `[texts] section_title` | `Comments` |
| Comment box placeholder | `[texts] input_placeholder` | `Add a comment…` |
| Text when there are no comments | `[texts] empty_text` | `No comments yet. Be the first!` |
| Text over a hidden spoiler | `[texts] spoiler_text` | `Spoiler: click to reveal` |

## Limits and backups

| Dashboard field | `comments.conf` | Default | Notes |
|---|---|---|---|
| Comments/edits per user per minute | `[limits] per_minute` | 6 | 0 = unlimited. Kept in memory, so it resets when the server restarts. |
| Comments/edits per user per hour | `[limits] per_hour` | 60 | 0 = unlimited |
| Daily database backups to keep | `[limits] backups_to_keep` | 14 | 0 = no backups. Backups go to `plugins/configurations/Jellyfin.Plugin.CozyComments/backups/`. |

## The settings file on the server

Jellyfin stores the settings as XML in
`<data folder>/plugins/configurations/Jellyfin.Plugin.CozyComments.xml`. You don't need to edit it
by hand. If you do, restart Jellyfin afterwards.
