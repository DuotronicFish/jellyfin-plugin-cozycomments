using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.CozyComments.Configuration;
using Jellyfin.Plugin.CozyComments.Data;
using Jellyfin.Plugin.CozyComments.Services;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CozyComments.Api;

/// <summary>REST API under <c>/CozyComments</c> plus the web client files.</summary>
[ApiController]
[Route("CozyComments")]
// Old address from before the rename (Lagtime Comments / Jellyfin Comments <= 1.1). Browser tabs that were open during
// the upgrade still run the old add-on, which calls this path; answering here keeps them working until they reload.
[Route("JellyfinComments")]
public partial class CommentsController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, (byte[] Bytes, string ETag)> s_assets = new();

    private readonly CommentStore _store;
    private readonly CoverStore _cover;
    private readonly WriteRateLimiter _limiter;
    private readonly IAuthorizationContext _auth;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _library;
    private readonly IImageProcessor _images;
    private readonly ILogger<CommentsController> _logger;
    private readonly Dictionary<Guid, (string Name, string? Tag)> _userCache = new();

    /// <summary>Initializes a new instance of the <see cref="CommentsController"/> class.</summary>
    /// <param name="store">Comment store.</param>
    /// <param name="cover">Cover image store.</param>
    /// <param name="limiter">Rate limiter.</param>
    /// <param name="auth">Authorization context.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="library">Library manager.</param>
    /// <param name="images">Image processor.</param>
    /// <param name="logger">Logger.</param>
    public CommentsController(
        CommentStore store,
        CoverStore cover,
        WriteRateLimiter limiter,
        IAuthorizationContext auth,
        IUserManager userManager,
        ILibraryManager library,
        IImageProcessor images,
        ILogger<CommentsController> logger)
    {
        _store = store;
        _cover = cover;
        _limiter = limiter;
        _auth = auth;
        _userManager = userManager;
        _library = library;
        _images = images;
        _logger = logger;
    }

    private static PluginConfiguration Cfg => Plugin.Settings;

    private static int MaxLength => Math.Clamp(Cfg.MaxCommentLength, 1, 10000);

    // ======================= static client files (anonymous: they hold no data) =======================

    internal static (byte[] Bytes, string ETag) Asset(string name) => s_assets.GetOrAdd(name, n =>
    {
        using var s = typeof(CommentsController).Assembly.GetManifestResourceStream("Jellyfin.Plugin.CozyComments.Web." + n)
            ?? throw new FileNotFoundException(n);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var bytes = ms.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        return (bytes, hash);
    });

    /// <summary>Cache-buster for the injected tags (changes with every build of the client files).</summary>
    /// <returns>A short hash.</returns>
    internal static string AssetVersion() => Asset("client.js").ETag + Asset("client.css").ETag[..4];

    /// <summary>The web client script.</summary>
    /// <returns>JavaScript.</returns>
    [HttpGet("client.js")]
    [AllowAnonymous]
    public IActionResult ClientJs()
    {
        var (bytes, tag) = Asset("client.js");
        return Serve(bytes, tag, "text/javascript; charset=utf-8", 86400);
    }

    /// <summary>The web client stylesheet.</summary>
    /// <returns>CSS.</returns>
    [HttpGet("client.css")]
    [AllowAnonymous]
    public IActionResult ClientCss()
    {
        var (bytes, tag) = Asset("client.css");
        return Serve(bytes, tag, "text/css; charset=utf-8", 86400);
    }

    /// <summary>The home card cover image (404 if none is set).</summary>
    /// <returns>The image.</returns>
    [HttpGet("Cover")]
    [AllowAnonymous]
    public IActionResult GetCover()
    {
        var c = _cover.Get();
        if (c is null)
        {
            return NotFound();
        }

        return Serve(c.Value.Bytes, c.Value.Tag, c.Value.Type, 86400);
    }

    private IActionResult Serve(byte[] bytes, string tag, string type, int maxAge)
    {
        var etag = "\"" + tag + "\"";
        Response.Headers.CacheControl = "public, max-age=" + maxAge;
        Response.Headers.ETag = etag;
        if (Request.Headers.IfNoneMatch.ToString() == etag)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(bytes, type);
    }

    // ======================= helpers =======================

    private async Task<User?> CurrentUserAsync()
    {
        var info = await _auth.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        if (!info.IsAuthenticated || info.User is null || info.IsApiKey)
        {
            return null;
        }

        return info.User;
    }

    /// <summary>True for an administrator session or an API key (both can already change any plugin setting).</summary>
    private async Task<bool> IsAdminCallerAsync()
    {
        var info = await _auth.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        if (!info.IsAuthenticated)
        {
            return false;
        }

        return info.IsApiKey || (info.User is not null && IsAdmin(info.User));
    }

    private static bool IsAdmin(User u) => u.HasPermission(PermissionKind.IsAdministrator);

    private static List<string> CommentableTypes()
    {
        var c = Cfg;
        var list = new List<string>();
        if (c.AllowOnMovies)
        {
            list.Add("Movie");
        }

        if (c.AllowOnSeries)
        {
            list.Add("Series");
        }

        if (c.AllowOnEpisodes)
        {
            list.Add("Episode");
        }

        if (c.AllowOnVideos)
        {
            list.Add("Video");
        }

        return list;
    }

    private static bool Commentable(BaseItem item)
    {
        var c = Cfg;
        return item switch
        {
            Movie => c.AllowOnMovies,
            Series => c.AllowOnSeries,
            Episode => c.AllowOnEpisodes,
            _ => item.GetType() == typeof(Video) && c.AllowOnVideos,
        };
    }

    private BaseItem? AccessibleItem(Guid itemId, User user) => _library.GetItemById<BaseItem>(itemId, user);

    private (string Name, string? Tag) UserInfo(Guid id)
    {
        if (_userCache.TryGetValue(id, out var v))
        {
            return v;
        }

        var u = _userManager.GetUserById(id);
        string? tag = null;
        if (u?.ProfileImage is not null)
        {
            try
            {
                tag = _images.GetImageCacheTag(u.ProfileImage.Path, u.ProfileImage.LastModified);
            }
            catch (Exception)
            {
                tag = null;
            }
        }

        v = (u?.Username ?? "Deleted user", tag);
        _userCache[id] = v;
        return v;
    }

    private CommentDto ToDto(CommentRow r, User viewer)
    {
        var (name, tag) = UserInfo(r.UserId);
        var mine = r.UserId == viewer.Id;
        var cfg = Cfg;
        return new CommentDto
        {
            Id = r.Id,
            ItemId = r.ItemId.ToString("N"),
            ParentId = r.ParentId,
            UserId = r.UserId.ToString("N"),
            UserName = name,
            ImageTag = tag,
            Text = r.Body,
            Stars = cfg.StarRatingsEnabled ? r.Stars : null,
            Spoiler = r.Spoiler,
            Created = r.CreatedMs,
            Edited = r.EditedMs,
            CanEdit = mine && cfg.EditingEnabled,
            CanDelete = (mine && cfg.DeletingEnabled) || IsAdmin(viewer),
        };
    }

    private static string? CleanText(string? text, out string? error)
    {
        error = null;
        var t = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
        {
            if (ch == '\n' || ch == '\t' || !char.IsControl(ch))
            {
                sb.Append(ch);
            }
        }

        t = sb.ToString().Trim();
        while (t.Contains("\n\n\n", StringComparison.Ordinal))
        {
            t = t.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        if (t.Length == 0)
        {
            error = "Comment is empty.";
            return null;
        }

        if (t.Length > MaxLength)
        {
            error = $"Comment is longer than {MaxLength} characters.";
            return null;
        }

        return t;
    }

    private static bool ValidStars(int? s) => s is null || (s >= 1 && s <= 5);

    [GeneratedRegex(@"^(#[0-9a-fA-F]{3,8}|[a-zA-Z]{3,30}|(rgb|rgba|hsl|hsla)\([0-9.,%\s/]{1,60}\))$")]
    private static partial Regex ColorRegex();

    private static string Color(string? value, string fallback)
    {
        var v = (value ?? string.Empty).Trim();
        return v.Length > 0 && ColorRegex().IsMatch(v) ? v : fallback;
    }

    private static string Text(string? value, string fallback, int max = 200)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length == 0)
        {
            return fallback;
        }

        return v.Length > max ? v[..max] : v;
    }

    private bool UnderRateLimit(Guid userId) => _limiter.TryHit(userId, Math.Max(0, Cfg.RateLimitPerMinute), Math.Max(0, Cfg.RateLimitPerHour));

    private ObjectResult Problem(int status, string message) => StatusCode(status, new { error = message });

    private static string RateLimitMessage(string what) =>
        $"Slow down a little: this server allows {Math.Max(0, Cfg.RateLimitPerMinute)} {what} per minute and {Math.Max(0, Cfg.RateLimitPerHour)} per hour. "
        + "Wait a minute and try again. (Admins can change the limits in Dashboard > Plugins > Cozy Comments.)";

    // ======================= settings + cover (admin) =======================

    /// <summary>The settings the web client needs (colours, texts, toggles).</summary>
    /// <returns>Client settings.</returns>
    [HttpGet("Settings")]
    [Authorize]
    public IActionResult GetSettings()
    {
        var c = Cfg;
        var defaults = new PluginConfiguration();
        var pageName = Text(c.PageName, defaults.PageName, 80);
        return Ok(new ClientSettingsDto
        {
            PageName = pageName,
            HomeCardLabel = Text(c.HomeCardLabel, pageName, 80),
            CommentsPageEnabled = c.CommentsPageEnabled,
            HomeCardAfterLibrary = (c.HomeCardAfterLibrary ?? string.Empty).Trim(),
            RenameMyMediaTo = Text(c.RenameMyMediaTo, string.Empty, 80),
            CoverTag = _cover.Get()?.Tag,
            PageBackgroundColor = Color(c.PageBackgroundColor, defaults.PageBackgroundColor),
            AccentColor = Color(c.AccentColor, defaults.AccentColor),
            StarColor = Color(c.StarColor, defaults.StarColor),
            ShowPostersOnPage = c.ShowPostersOnPage,
            PageSize = Math.Clamp(c.PageSize, 1, 50),
            StarRatingsEnabled = c.StarRatingsEnabled,
            SpoilersEnabled = c.SpoilersEnabled,
            RepliesEnabled = c.RepliesEnabled,
            EditingEnabled = c.EditingEnabled,
            MaxCommentLength = MaxLength,
            PreviewWordLimit = Math.Max(0, c.PreviewWordLimit),
            ShowMoreText = Text(c.ShowMoreText, defaults.ShowMoreText, 60),
            CollapseDetailSections = c.CollapseDetailSections,
            CommentableTypes = CommentableTypes(),
            SectionTitle = Text(c.SectionTitle, defaults.SectionTitle, 80),
            InputPlaceholder = Text(c.InputPlaceholder, defaults.InputPlaceholder),
            EmptyText = Text(c.EmptyText, defaults.EmptyText),
            SpoilerText = Text(c.SpoilerText, defaults.SpoilerText),
            ClientVersion = AssetVersion(),
        });
    }

    /// <summary>Uploads the home card cover image (raw JPEG/PNG/WebP/GIF body, max 5 MB). Admins and API keys only.</summary>
    /// <returns>The new cover tag.</returns>
    [HttpPost("Cover")]
    [Authorize]
    public async Task<IActionResult> UploadCover()
    {
        if (!await IsAdminCallerAsync().ConfigureAwait(false))
        {
            return Problem(403, "Only administrators can change the cover image.");
        }

        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await Request.Body.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > CoverStore.MaxBytes)
            {
                return Problem(413, "Image is larger than 5 MB.");
            }
        }

        var bytes = ms.ToArray();
        var ext = CoverStore.Sniff(bytes);
        if (ext is null)
        {
            return Problem(400, "Upload a JPEG, PNG, WebP or GIF image.");
        }

        _cover.Save(bytes, ext);
        return Ok(new { coverTag = _cover.Get()?.Tag });
    }

    /// <summary>Removes the cover image (the home card shows an icon instead). Admins and API keys only.</summary>
    /// <returns>No content.</returns>
    [HttpDelete("Cover")]
    [Authorize]
    public async Task<IActionResult> DeleteCover()
    {
        if (!await IsAdminCallerAsync().ConfigureAwait(false))
        {
            return Problem(403, "Only administrators can change the cover image.");
        }

        _cover.Delete();
        return NoContent();
    }

    // ======================= comments =======================

    /// <summary>All comments of an item (top-level newest first, each with its replies oldest first).</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>The comments.</returns>
    [HttpGet("Items/{itemId}/Comments")]
    [Authorize]
    public async Task<IActionResult> GetItemComments([FromRoute] Guid itemId)
    {
        var user = await CurrentUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Problem(401, "You are not signed in, or your session has expired. Sign out of Jellyfin and sign in again, then retry.");
        }

        var item = AccessibleItem(itemId, user);
        if (item is null)
        {
            return Problem(404, "This item was not found. It may have been removed from the server, or your account can't see its library. Refresh the page; if it keeps happening, ask an admin to check your library access (Dashboard > Users > your user > Access).");
        }

        var rows = _store.GetForItem(itemId);
        var top = rows.Where(r => r.ParentId is null).Select(r => ToDto(r, user)).ToList();
        var byParent = rows.Where(r => r.ParentId is not null)
            .OrderBy(r => r.CreatedMs).ThenBy(r => r.Id)
            .GroupBy(r => r.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(r => ToDto(r, user)).ToList());
        foreach (var t in top)
        {
            t.Replies = byParent.TryGetValue(t.Id, out var reps) ? reps : new List<CommentDto>();
        }

        return Ok(new { itemId = itemId.ToString("N"), commentable = Commentable(item), total = rows.Count, comments = top });
    }

    /// <summary>Posts a comment or reply.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="input">The comment.</param>
    /// <returns>The new comment.</returns>
    [HttpPost("Items/{itemId}/Comments")]
    [Authorize]
    public async Task<IActionResult> Post([FromRoute] Guid itemId, [FromBody] CommentInput input)
    {
        var user = await CurrentUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Problem(401, "You are not signed in, or your session has expired. Sign out of Jellyfin and sign in again, then retry.");
        }

        var item = AccessibleItem(itemId, user);
        if (item is null)
        {
            return Problem(404, "This item was not found. It may have been removed from the server, or your account can't see its library. Refresh the page; if it keeps happening, ask an admin to check your library access (Dashboard > Users > your user > Access).");
        }

        if (!Commentable(item))
        {
            return Problem(400, "Comments are turned off for this kind of item. An admin can allow them in Dashboard > Plugins > Cozy Comments > Allow comments on.");
        }

        var cfg = Cfg;
        var text = CleanText(input.Text, out var err);
        if (text is null)
        {
            return Problem(400, err!);
        }

        if (!ValidStars(input.Stars))
        {
            return Problem(400, "Stars must be 1 to 5.");
        }

        long? parentId = null;
        int? stars = cfg.StarRatingsEnabled ? input.Stars : null;
        if (input.ParentId is long pid)
        {
            if (!cfg.RepliesEnabled)
            {
                return Problem(403, "Replies are turned off on this server. An admin can turn them on in Dashboard > Plugins > Cozy Comments.");
            }

            var parent = _store.Get(pid);
            if (parent is null || parent.ItemId != itemId)
            {
                return Problem(400, "The comment you replied to was deleted. Refresh the page to see the current comments.");
            }

            // One level deep: a reply to a reply attaches to the top-level comment.
            parentId = parent.ParentId ?? parent.Id;
            stars = null; // stars belong to top-level comments only
        }

        if (!UnderRateLimit(user.Id))
        {
            return Problem(429, RateLimitMessage("comments"));
        }

        var row = await _store.InsertAsync(new CommentRow
        {
            ItemId = itemId,
            UserId = user.Id,
            ParentId = parentId,
            Body = text,
            Stars = stars,
            Spoiler = cfg.SpoilersEnabled && input.Spoiler,
            CreatedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        }).ConfigureAwait(false);
        return Ok(ToDto(row, user));
    }

    /// <summary>Edits one of the caller's own comments.</summary>
    /// <param name="id">Comment id.</param>
    /// <param name="input">New values.</param>
    /// <returns>The updated comment.</returns>
    [HttpPut("Comments/{id:long}")]
    [Authorize]
    public async Task<IActionResult> Edit([FromRoute] long id, [FromBody] CommentInput input)
    {
        var user = await CurrentUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Problem(401, "You are not signed in, or your session has expired. Sign out of Jellyfin and sign in again, then retry.");
        }

        var cfg = Cfg;
        if (!cfg.EditingEnabled)
        {
            return Problem(403, "Editing comments is turned off on this server. An admin can turn it on in Dashboard > Plugins > Cozy Comments.");
        }

        var row = _store.Get(id);
        if (row is null || AccessibleItem(row.ItemId, user) is null)
        {
            return Problem(404, "This comment no longer exists (it may have been deleted). Refresh the page to see the current comments.");
        }

        if (row.UserId != user.Id)
        {
            return Problem(403, "You can only edit your own comments.");
        }

        var text = CleanText(input.Text, out var err);
        if (text is null)
        {
            return Problem(400, err!);
        }

        if (!ValidStars(input.Stars))
        {
            return Problem(400, "Stars must be 1 to 5.");
        }

        if (!UnderRateLimit(user.Id))
        {
            return Problem(429, RateLimitMessage("edits"));
        }

        // Turned-off features keep whatever the comment already had.
        var stars = row.ParentId is not null ? null : (cfg.StarRatingsEnabled ? input.Stars : row.Stars);
        var spoiler = cfg.SpoilersEnabled ? input.Spoiler : row.Spoiler;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _store.UpdateAsync(id, text, stars, spoiler, now).ConfigureAwait(false);
        row.Body = text;
        row.Stars = stars;
        row.Spoiler = spoiler;
        row.EditedMs = now;
        return Ok(ToDto(row, user));
    }

    /// <summary>Deletes a comment (own, or any for administrators). Deleting a top-level comment also deletes its replies.</summary>
    /// <param name="id">Comment id.</param>
    /// <returns>No content.</returns>
    [HttpDelete("Comments/{id:long}")]
    [Authorize]
    public async Task<IActionResult> Delete([FromRoute] long id)
    {
        var user = await CurrentUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Problem(401, "You are not signed in, or your session has expired. Sign out of Jellyfin and sign in again, then retry.");
        }

        var row = _store.Get(id);
        var admin = IsAdmin(user);
        if (row is null || (!admin && AccessibleItem(row.ItemId, user) is null))
        {
            return Problem(404, "This comment no longer exists (it may have been deleted). Refresh the page to see the current comments.");
        }

        if (!admin && (row.UserId != user.Id || !Cfg.DeletingEnabled))
        {
            return Problem(403, row.UserId != user.Id ? "You can only delete your own comments." : "Deleting comments is turned off on this server. An admin can turn it on in Dashboard > Plugins > Cozy Comments.");
        }

        var n = await _store.DeleteAsync(id).ConfigureAwait(false);
        if (row.UserId != user.Id)
        {
            _logger.LogInformation("[Cozy Comments] Admin {Admin} deleted comment {Id} by user {User} ({N} rows)", user.Username, id, row.UserId, n);
        }

        return NoContent();
    }

    /// <summary>The comments page: newest first, only for items the caller can access.</summary>
    /// <param name="startIndex">Paging offset.</param>
    /// <param name="limit">Page size (max 50).</param>
    /// <returns>One page.</returns>
    [HttpGet("Recent")]
    [Authorize]
    public async Task<IActionResult> Recent([FromQuery] int startIndex = 0, [FromQuery] int limit = 20)
    {
        var user = await CurrentUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return Problem(401, "You are not signed in, or your session has expired. Sign out of Jellyfin and sign in again, then retry.");
        }

        var cfg = Cfg;
        if (!cfg.CommentsPageEnabled)
        {
            return Problem(403, "The comments page is turned off on this server. An admin can turn it on in Dashboard > Plugins > Cozy Comments > Comments page, then reload.");
        }

        startIndex = Math.Max(0, startIndex);
        limit = Math.Clamp(limit, 1, 50);

        var index = _store.GetRecentIndex(5000, cfg.ShowRepliesOnPage, cfg.ShowSpoilersOnPage);
        var access = new Dictionary<Guid, BaseItem?>();
        var visible = new List<long>();
        foreach (var (cid, itemId) in index)
        {
            if (!access.TryGetValue(itemId, out var it))
            {
                it = AccessibleItem(itemId, user);
                access[itemId] = it;
            }

            if (it is not null)
            {
                visible.Add(cid);
            }
        }

        var pageIds = visible.Skip(startIndex).Take(limit).ToList();
        var rows = _store.GetMany(pageIds);
        var items = new List<CommentDto>();
        foreach (var r in rows)
        {
            var it = access[r.ItemId]!;
            var dto = ToDto(r, user);
            dto.ItemName = DisplayName(it);
            dto.ItemType = it.GetType().Name;
            if (cfg.ShowPostersOnPage)
            {
                SetPoster(dto, it);
            }

            items.Add(dto);
        }

        return Ok(new { startIndex, totalRecordCount = visible.Count, items });
    }

    private void SetPoster(CommentDto dto, BaseItem it)
    {
        try
        {
            BaseItem src = it is Episode ep && ep.Series is not null ? ep.Series : it;
            dto.PosterId = src.Id.ToString("N");
            var info = src.GetImageInfo(ImageType.Primary, 0);
            dto.PosterTag = info is null ? null : _images.GetImageCacheTag(src, info);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Cozy Comments] Poster lookup failed for {Id}", it.Id);
            dto.PosterTag = null;
        }
    }

    private static string DisplayName(BaseItem it)
    {
        if (it is Episode ep)
        {
            var s = ep.ParentIndexNumber is int sn ? $"S{sn:00}" : string.Empty;
            var e = ep.IndexNumber is int en ? $"E{en:00}" : string.Empty;
            var code = (s + e).Length > 0 ? " " + s + e : string.Empty;
            return $"{ep.SeriesName}{code} - {ep.Name}";
        }

        return it.ProductionYear is int y && it is not Series ? $"{it.Name} ({y})" : it.Name ?? "Unknown";
    }
}
