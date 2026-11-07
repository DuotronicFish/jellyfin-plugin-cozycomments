using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CozyComments.Configuration;

/// <summary>
/// All settings of Cozy Comments. Edit them in Dashboard → Plugins → Cozy Comments,
/// or push a <c>comments.conf</c> file with <c>scripts/configure.py</c>.
/// Every setting is read on each request, so changes apply without a server restart
/// (browsers pick them up on their next page load).
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // ---------------- comments page (the global "recent comments" feed) ----------------

    /// <summary>Gets or sets a value indicating whether the comments page is on. Off = no home screen card, and the page's API returns 404.
    /// Comments on movie / show pages are not affected.</summary>
    public bool CommentsPageEnabled { get; set; } = true;

    /// <summary>Gets or sets the title of the comments page, also used as the home card label unless <see cref="HomeCardLabel"/> is set.</summary>
    public string PageName { get; set; } = "Recent Comments";

    /// <summary>Gets or sets the background colour of the comments page (CSS colour, e.g. <c>#101010</c>).</summary>
    public string PageBackgroundColor { get; set; } = "#101010";

    /// <summary>Gets or sets a value indicating whether the item poster is shown next to each comment on the comments page.</summary>
    public bool ShowPostersOnPage { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether comments marked as spoilers appear (blurred) on the comments page.</summary>
    public bool ShowSpoilersOnPage { get; set; }

    /// <summary>Gets or sets a value indicating whether replies appear on the comments page (top-level comments always do).</summary>
    public bool ShowRepliesOnPage { get; set; }

    /// <summary>Gets or sets the number of comments loaded per "Load more" on the comments page (1-50).</summary>
    public int PageSize { get; set; } = 20;

    // ---------------- home screen card ----------------

    /// <summary>Gets or sets the label of the home card. Empty = use <see cref="PageName"/>.</summary>
    public string HomeCardLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the library the home card is placed after. Empty = last position.</summary>
    public string HomeCardAfterLibrary { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional new heading for the "My Media" home section. Empty = leave it alone.</summary>
    public string RenameMyMediaTo { get; set; } = string.Empty;

    // ---------------- colours ----------------

    /// <summary>Gets or sets the accent colour (buttons, links, focus outlines).</summary>
    public string AccentColor { get; set; } = "#00a4dc";

    /// <summary>Gets or sets the colour of filled rating stars.</summary>
    public string StarColor { get; set; } = "#f5c518";

    // ---------------- features ----------------

    /// <summary>Gets or sets a value indicating whether users can attach an optional 1-5 star rating to a top-level comment.</summary>
    public bool StarRatingsEnabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users can mark comments as spoilers (shown blurred until clicked).</summary>
    public bool SpoilersEnabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users can reply to comments (one level deep).</summary>
    public bool RepliesEnabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users can edit their own comments.</summary>
    public bool EditingEnabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users can delete their own comments (administrators can always delete any comment).</summary>
    public bool DeletingEnabled { get; set; } = true;

    /// <summary>Gets or sets the maximum number of characters in one comment (1-10000).</summary>
    public int MaxCommentLength { get; set; } = 2000;

    /// <summary>Gets or sets the number of words shown before a long comment is cut with a "show more" button. 0 = never cut.</summary>
    public int PreviewWordLimit { get; set; } = 30;

    /// <summary>Gets or sets the text of the button that expands a long comment.</summary>
    public string ShowMoreText { get; set; } = "show more";

    /// <summary>Gets or sets a value indicating whether Cast &amp; Crew, Special Features etc. on detail pages get a +/- button and start collapsed.</summary>
    public bool CollapseDetailSections { get; set; }

    // ---------------- where comments are allowed ----------------

    /// <summary>Gets or sets a value indicating whether movies can be commented on.</summary>
    public bool AllowOnMovies { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether TV series can be commented on.</summary>
    public bool AllowOnSeries { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether TV episodes can be commented on.</summary>
    public bool AllowOnEpisodes { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether plain videos (home videos libraries) can be commented on.</summary>
    public bool AllowOnVideos { get; set; } = true;

    // ---------------- texts ----------------

    /// <summary>Gets or sets the heading of the comments section on detail pages.</summary>
    public string SectionTitle { get; set; } = "Comments";

    /// <summary>Gets or sets the placeholder of the comment box.</summary>
    public string InputPlaceholder { get; set; } = "Add a comment…";

    /// <summary>Gets or sets the text shown when an item has no comments.</summary>
    public string EmptyText { get; set; } = "No comments yet. Be the first!";

    /// <summary>Gets or sets the text shown over a hidden spoiler.</summary>
    public string SpoilerText { get; set; } = "Spoiler: click to reveal";

    // ---------------- limits and maintenance ----------------

    /// <summary>Gets or sets the number of comments/edits one user may post per minute (0 = unlimited).</summary>
    public int RateLimitPerMinute { get; set; } = 6;

    /// <summary>Gets or sets the number of comments/edits one user may post per hour (0 = unlimited).</summary>
    public int RateLimitPerHour { get; set; } = 60;

    /// <summary>Gets or sets the number of daily database backups to keep (0 = no backups).</summary>
    public int BackupsToKeep { get; set; } = 14;
}
