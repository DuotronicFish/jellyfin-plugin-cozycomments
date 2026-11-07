# HTTP API

All endpoints live under `/CozyComments` on your Jellyfin server and use Jellyfin's normal authentication
(`Authorization: MediaBrowser Token="<token>"`).

- **Comment endpoints** need a signed-in **user** session. API keys are refused, because a comment
  always belongs to a person.
- **Item access** is checked on every call with Jellyfin's own rules (library access and parental controls).
  Items a user can't see return `404`, and their comments are left out of `Recent`.
- Errors come back as `{"error": "message"}` with a matching status code.
- Jellyfin leaves `null` fields out of its JSON, so `stars`, `parentId` and similar fields may be missing.

## Comments

### `GET /CozyComments/Items/{itemId}/Comments`

All comments on an item: top-level comments newest first, each with its `replies` oldest first.

```json
{
  "itemId": "0d2f…",
  "commentable": true,
  "total": 3,
  "comments": [
    {
      "id": 12, "itemId": "0d2f…", "userId": "7a1c…", "userName": "alex", "imageTag": "…",
      "text": "Loved the ending", "stars": 5, "spoiler": false,
      "created": 1767225600000, "edited": 1767229200000,
      "canEdit": true, "canDelete": true,
      "replies": [ { "id": 13, "parentId": 12, "…": "…" } ]
    }
  ]
}
```

Times are Unix milliseconds (UTC). `commentable` is `false` when comments are turned off for that item type.

### `POST /CozyComments/Items/{itemId}/Comments`

```json
{ "text": "Loved it", "stars": 4, "spoiler": false, "parentId": null }
```

- `text`: required. It's trimmed, control characters are removed, and three or more blank lines in a row
  are shortened to two. The maximum length is the *Max comment length* setting.
- `stars`: optional, 1–5. Only top-level comments keep stars, and stars are ignored when star ratings are off.
- `spoiler`: ignored when spoilers are off.
- `parentId`: makes the comment a reply. Replies are one level deep, so a reply to a reply is attached to the
  top-level comment. Returns `403` when replies are off.

Returns the new comment. Status codes: `400` invalid, `403` turned off, `404` no access, `429` rate limit.

### `PUT /CozyComments/Comments/{id}`

Same body as `POST` (without `parentId`). Users can only edit their own comments. Returns `403` if editing is off.

### `DELETE /CozyComments/Comments/{id}`

Users can delete their own comments (when deleting is on), and admins can delete any comment. Deleting a
top-level comment also deletes its replies. Returns `204`.

### `GET /CozyComments/Recent?startIndex=0&limit=20`

Returns `404` when the comments page is turned off. Otherwise: the newest comments on every item the caller can access, `limit` at most 50. Whether
replies and spoilers are included depends on the settings. Each entry also has `itemName`, `itemType`, and
(when posters are on) `posterId` and `posterTag`.

```json
{ "startIndex": 0, "totalRecordCount": 57, "items": [ { "id": 12, "itemName": "Some Film (2024)", "…": "…" } ] }
```

## Settings and the card image

### `GET /CozyComments/Settings`

The settings the web app needs: texts, colours, switches and `coverTag`. Any signed-in user can read them.
To change settings, use Jellyfin's own `GET`/`POST /Plugins/dc9c7d6f-2de4-4ee4-9187-6a87ca998510/Configuration`
(admins or an API key).

### `GET /CozyComments/Cover`

The card image (no sign-in needed, since it's shown on the home screen). Returns `404` when none is set.

### `POST /CozyComments/Cover`

Admins or an API key only. Send the raw image as the body, with its `Content-Type`
(JPEG/PNG/WebP/GIF, max 5 MB). The file type is checked from its contents.

```bash
curl -X POST -H 'Authorization: MediaBrowser Token="<api key>"' -H 'Content-Type: image/jpeg' \
     --data-binary @cover.jpg http://localhost:8096/CozyComments/Cover
```

### `DELETE /CozyComments/Cover`

Removes the image, so the card shows an icon again.

## Web client files

`GET /CozyComments/client.js` and `client.css` are the web app add-on. They need no sign-in because they
contain no data. File Transformation adds them to `/web/index.html` with a `?v=<hash>` cache-buster.
