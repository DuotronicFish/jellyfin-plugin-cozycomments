#!/usr/bin/env python3
"""Apply a comments.conf settings file to the Cozy Comments plugin on a Jellyfin server.

    python3 scripts/configure.py comments.conf            # apply
    python3 scripts/configure.py comments.conf --dry-run  # show what would change, change nothing
    python3 scripts/configure.py comments.conf --show     # print the server's current settings

The server address and API key come from the [server] section of the file, or from
--url / --api-key, or from the JELLYFIN_URL / JELLYFIN_API_KEY environment variables.
Only the Python 3 standard library is used (works on Linux, macOS and Windows).
"""
import argparse
import configparser
import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

PLUGIN_ID = "dc9c7d6f-2de4-4ee4-9187-6a87ca998510"

# (section, key) -> (setting name on the server, type, extra)
SCHEMA = {
    ("page", "name"): ("PageName", "text", 80),
    ("page", "background_color"): ("PageBackgroundColor", "color", None),
    ("page", "show_posters"): ("ShowPostersOnPage", "bool", None),
    ("page", "show_spoilers"): ("ShowSpoilersOnPage", "bool", None),
    ("page", "show_replies"): ("ShowRepliesOnPage", "bool", None),
    ("page", "page_size"): ("PageSize", "int", (1, 50)),
    ("page", "enabled"): ("CommentsPageEnabled", "bool", None),
    ("home", "card_label"): ("HomeCardLabel", "text", 80),
    ("home", "card_after_library"): ("HomeCardAfterLibrary", "text", 120),
    ("home", "rename_my_media_to"): ("RenameMyMediaTo", "text", 80),
    ("colors", "accent_color"): ("AccentColor", "color", None),
    ("colors", "star_color"): ("StarColor", "color", None),
    ("features", "star_ratings"): ("StarRatingsEnabled", "bool", None),
    ("features", "spoilers"): ("SpoilersEnabled", "bool", None),
    ("features", "replies"): ("RepliesEnabled", "bool", None),
    ("features", "editing"): ("EditingEnabled", "bool", None),
    ("features", "deleting"): ("DeletingEnabled", "bool", None),
    ("features", "max_comment_length"): ("MaxCommentLength", "int", (1, 10000)),
    ("features", "preview_word_limit"): ("PreviewWordLimit", "int", (0, 10000)),
    ("features", "show_more_text"): ("ShowMoreText", "text", 60),
    ("features", "collapse_detail_sections"): ("CollapseDetailSections", "bool", None),
    ("allow_comments_on", "movies"): ("AllowOnMovies", "bool", None),
    ("allow_comments_on", "series"): ("AllowOnSeries", "bool", None),
    ("allow_comments_on", "episodes"): ("AllowOnEpisodes", "bool", None),
    ("allow_comments_on", "videos"): ("AllowOnVideos", "bool", None),
    ("texts", "section_title"): ("SectionTitle", "text", 80),
    ("texts", "input_placeholder"): ("InputPlaceholder", "text", 200),
    ("texts", "empty_text"): ("EmptyText", "text", 200),
    ("texts", "spoiler_text"): ("SpoilerText", "text", 200),
    ("limits", "per_minute"): ("RateLimitPerMinute", "int", (0, 1000)),
    ("limits", "per_hour"): ("RateLimitPerHour", "int", (0, 100000)),
    ("limits", "backups_to_keep"): ("BackupsToKeep", "int", (0, 365)),
}
# Text settings that may be empty on purpose (empty = "use the default behaviour").
MAY_BE_EMPTY = {"HomeCardLabel", "HomeCardAfterLibrary", "RenameMyMediaTo"}

COLOR_RE = re.compile(r"^(#[0-9a-fA-F]{3,8}|[a-zA-Z]{3,30}|(rgb|rgba|hsl|hsla)\([0-9.,%\s/]{1,60}\))$")
IMAGE_TYPES = {".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".png": "image/png", ".webp": "image/webp", ".gif": "image/gif"}
TRUE = {"1", "true", "yes", "on", "y"}
FALSE = {"0", "false", "no", "off", "n"}


class ConfigError(Exception):
    pass


def parse_value(name, kind, extra, raw):
    v = raw.strip()
    if kind == "bool":
        if v.lower() in TRUE:
            return True
        if v.lower() in FALSE:
            return False
        raise ConfigError(f"{name}: expected yes/no, got {raw!r}")
    if kind == "int":
        try:
            n = int(v)
        except ValueError:
            raise ConfigError(f"{name}: expected a whole number, got {raw!r}") from None
        lo, hi = extra
        if not lo <= n <= hi:
            raise ConfigError(f"{name}: must be between {lo} and {hi}, got {n}")
        return n
    if kind == "color":
        if not COLOR_RE.match(v):
            raise ConfigError(f"{name}: not a colour I understand: {raw!r} (try #rrggbb)")
        return v
    if len(v) > extra:
        raise ConfigError(f"{name}: longer than {extra} characters")
    return v


class Jellyfin:
    def __init__(self, url, key):
        self.url = url.rstrip("/")
        self.key = key

    def request(self, method, path, body=None, content_type="application/json"):
        data = None
        headers = {"Authorization": f'MediaBrowser Token="{self.key}"', "Accept": "application/json"}
        if body is not None:
            data = body if isinstance(body, bytes) else json.dumps(body).encode("utf-8")
            headers["Content-Type"] = content_type
        req = urllib.request.Request(self.url + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                raw = r.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")[:300]
            if e.code == 401:
                raise SystemExit(f"Jellyfin refused the API key (401). Check api_key / JELLYFIN_API_KEY.") from None
            raise SystemExit(f"{method} {path} failed: HTTP {e.code} {detail}") from None
        except urllib.error.URLError as e:
            raise SystemExit(f"Can't reach Jellyfin at {self.url}: {e.reason}") from None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("config", nargs="?", default="comments.conf", help="settings file (default: comments.conf)")
    ap.add_argument("--url", help="Jellyfin address (overrides the file)")
    ap.add_argument("--api-key", help="Jellyfin API key (overrides the file)")
    ap.add_argument("--dry-run", action="store_true", help="show the changes without applying them")
    ap.add_argument("--show", action="store_true", help="print the current settings on the server and exit")
    a = ap.parse_args()

    cp = configparser.ConfigParser(interpolation=None)
    cfg_path = Path(a.config)
    if cfg_path.exists():
        with cfg_path.open(encoding="utf-8") as f:
            cp.read_file(f)
    elif not a.show:
        raise SystemExit(f"{cfg_path} not found. Copy comments.conf.example to comments.conf and edit it.")

    url = a.url or os.environ.get("JELLYFIN_URL") or cp.get("server", "url", fallback="").strip() or "http://localhost:8096"
    key = a.api_key or os.environ.get("JELLYFIN_API_KEY") or cp.get("server", "api_key", fallback="").strip()
    if not key:
        raise SystemExit("No API key. Create one in Dashboard > API Keys, then put it in [server] api_key "
                         "or set JELLYFIN_API_KEY.")
    jf = Jellyfin(url, key)

    plugins = jf.request("GET", "/Plugins") or []
    mine = next((p for p in plugins if p.get("Id", "").replace("-", "").lower() == PLUGIN_ID.replace("-", "")), None)
    if mine is None:
        raise SystemExit("The Cozy Comments plugin is not installed on this server. Install it first (see README).")
    print(f"Server {url}: Cozy Comments {mine.get('Version')} ({mine.get('Status')})")
    if mine.get("Status") not in ("Active", "Restart"):
        print("  Warning: the plugin is not active. Check Dashboard > Plugins.")
    if not any(p.get("Name") == "File Transformation" for p in plugins):
        print("  Warning: the 'File Transformation' plugin isn't installed; comments won't show in the web app without it.")

    current = jf.request("GET", f"/Plugins/{PLUGIN_ID}/Configuration")
    if a.show:
        print(json.dumps(current, indent=2, ensure_ascii=False))
        return

    # Parse the file; report every problem at once.
    errors, updates = [], {}
    known = {(s, k) for s, k in SCHEMA} | {("server", "url"), ("server", "api_key"), ("page", "image")}
    for section in cp.sections():
        for k, raw in cp.items(section):
            if (section, k) not in known:
                errors.append(f"[{section}] {k}: unknown setting (typo?)")
                continue
            if (section, k) not in SCHEMA:
                continue
            name, kind, extra = SCHEMA[(section, k)]
            if raw.strip() == "" and not (kind == "text" and name in MAY_BE_EMPTY):
                continue  # left empty = keep the server's value
            try:
                updates[name] = parse_value(f"[{section}] {k}", kind, extra, raw)
            except ConfigError as e:
                errors.append(str(e))

    image = cp.get("page", "image", fallback="").strip()
    image_bytes = image_type = None
    if image and image.lower() != "none":
        p = Path(image).expanduser()
        if not p.is_absolute():
            p = (cfg_path.parent / p).resolve()
        if not p.is_file():
            errors.append(f"[page] image: file not found: {p}")
        elif p.suffix.lower() not in IMAGE_TYPES:
            errors.append(f"[page] image: use a .jpg, .png, .webp or .gif file ({p.name})")
        elif p.stat().st_size > 5 * 1024 * 1024:
            errors.append(f"[page] image: larger than 5 MB ({p.name})")
        else:
            image_bytes, image_type = p.read_bytes(), IMAGE_TYPES[p.suffix.lower()]

    if errors:
        print("Problems in " + str(cfg_path) + ":")
        for e in errors:
            print("  - " + e)
        sys.exit(1)

    changed = {k: v for k, v in updates.items() if current.get(k) != v}
    if not changed:
        print("Settings: already up to date.")
    for k, v in changed.items():
        print(f"  {k}: {current.get(k)!r} -> {v!r}")
    if image_bytes is not None:
        print(f"  Cover image: upload {image} ({len(image_bytes) // 1024} KB)")
    elif image.lower() == "none":
        print("  Cover image: remove")

    if a.dry_run:
        print("Dry run: nothing was changed.")
        return

    if changed:
        current.update(changed)
        jf.request("POST", f"/Plugins/{PLUGIN_ID}/Configuration", current)
    if image_bytes is not None:
        jf.request("POST", "/CozyComments/Cover", image_bytes, content_type=image_type)
    elif image.lower() == "none":
        jf.request("DELETE", "/CozyComments/Cover")
    print("Done. Users see the changes the next time they load or refresh Jellyfin.")


if __name__ == "__main__":
    main()
