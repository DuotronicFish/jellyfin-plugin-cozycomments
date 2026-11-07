using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.CozyComments.Api;

/// <summary>Body of a new comment, reply or edit.</summary>
public sealed class CommentInput
{
    /// <summary>Gets or sets the text.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Gets or sets the optional 1-5 star rating (top-level comments only).</summary>
    [JsonPropertyName("stars")]
    public int? Stars { get; set; }

    /// <summary>Gets or sets a value indicating whether the comment is a spoiler.</summary>
    [JsonPropertyName("spoiler")]
    public bool Spoiler { get; set; }

    /// <summary>Gets or sets the comment being replied to.</summary>
    [JsonPropertyName("parentId")]
    public long? ParentId { get; set; }
}

/// <summary>A comment as returned by the API.</summary>
public sealed class CommentDto
{
    /// <summary>Gets or sets the id.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the item id (32 hex).</summary>
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Gets or sets the parent comment id.</summary>
    [JsonPropertyName("parentId")]
    public long? ParentId { get; set; }

    /// <summary>Gets or sets the author id.</summary>
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the author name.</summary>
    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the author's profile image tag.</summary>
    [JsonPropertyName("imageTag")]
    public string? ImageTag { get; set; }

    /// <summary>Gets or sets the text.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the stars.</summary>
    [JsonPropertyName("stars")]
    public int? Stars { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a spoiler.</summary>
    [JsonPropertyName("spoiler")]
    public bool Spoiler { get; set; }

    /// <summary>Gets or sets the creation time (Unix ms).</summary>
    [JsonPropertyName("created")]
    public long Created { get; set; }

    /// <summary>Gets or sets the edit time (Unix ms).</summary>
    [JsonPropertyName("edited")]
    public long? Edited { get; set; }

    /// <summary>Gets or sets a value indicating whether the caller may edit it.</summary>
    [JsonPropertyName("canEdit")]
    public bool CanEdit { get; set; }

    /// <summary>Gets or sets a value indicating whether the caller may delete it.</summary>
    [JsonPropertyName("canDelete")]
    public bool CanDelete { get; set; }

    /// <summary>Gets or sets the replies (item page only).</summary>
    [JsonPropertyName("replies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CommentDto>? Replies { get; set; }

    /// <summary>Gets or sets the item display name (comments page only).</summary>
    [JsonPropertyName("itemName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ItemName { get; set; }

    /// <summary>Gets or sets the item type (comments page only).</summary>
    [JsonPropertyName("itemType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ItemType { get; set; }

    /// <summary>Gets or sets the item whose Primary image is the poster (the series for episodes).</summary>
    [JsonPropertyName("posterId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PosterId { get; set; }

    /// <summary>Gets or sets the poster image tag (null = no poster).</summary>
    [JsonPropertyName("posterTag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PosterTag { get; set; }
}

/// <summary>The subset of the settings the web client needs.</summary>
public sealed class ClientSettingsDto
{
    /// <summary>Gets or sets the comments page title.</summary>
    [JsonPropertyName("pageName")]
    public string PageName { get; set; } = string.Empty;

    /// <summary>Gets or sets the home card label.</summary>
    [JsonPropertyName("homeCardLabel")]
    public string HomeCardLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the comments page (and its home card) is on.</summary>
    [JsonPropertyName("commentsPageEnabled")]
    public bool CommentsPageEnabled { get; set; }

    /// <summary>Gets or sets the library the home card follows.</summary>
    [JsonPropertyName("homeCardAfterLibrary")]
    public string HomeCardAfterLibrary { get; set; } = string.Empty;

    /// <summary>Gets or sets the new "My Media" heading.</summary>
    [JsonPropertyName("renameMyMediaTo")]
    public string RenameMyMediaTo { get; set; } = string.Empty;

    /// <summary>Gets or sets the cover image tag (null = no cover image).</summary>
    [JsonPropertyName("coverTag")]
    public string? CoverTag { get; set; }

    /// <summary>Gets or sets the page background colour.</summary>
    [JsonPropertyName("pageBackgroundColor")]
    public string PageBackgroundColor { get; set; } = string.Empty;

    /// <summary>Gets or sets the accent colour.</summary>
    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = string.Empty;

    /// <summary>Gets or sets the star colour.</summary>
    [JsonPropertyName("starColor")]
    public string StarColor { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether posters show on the comments page.</summary>
    [JsonPropertyName("showPostersOnPage")]
    public bool ShowPostersOnPage { get; set; }

    /// <summary>Gets or sets the page size of the comments page.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    /// <summary>Gets or sets a value indicating whether star ratings are on.</summary>
    [JsonPropertyName("starRatingsEnabled")]
    public bool StarRatingsEnabled { get; set; }

    /// <summary>Gets or sets a value indicating whether spoilers are on.</summary>
    [JsonPropertyName("spoilersEnabled")]
    public bool SpoilersEnabled { get; set; }

    /// <summary>Gets or sets a value indicating whether replies are on.</summary>
    [JsonPropertyName("repliesEnabled")]
    public bool RepliesEnabled { get; set; }

    /// <summary>Gets or sets a value indicating whether editing is on.</summary>
    [JsonPropertyName("editingEnabled")]
    public bool EditingEnabled { get; set; }

    /// <summary>Gets or sets the max comment length.</summary>
    [JsonPropertyName("maxCommentLength")]
    public int MaxCommentLength { get; set; }

    /// <summary>Gets or sets the preview word limit.</summary>
    [JsonPropertyName("previewWordLimit")]
    public int PreviewWordLimit { get; set; }

    /// <summary>Gets or sets the "show more" text.</summary>
    [JsonPropertyName("showMoreText")]
    public string ShowMoreText { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether detail sections are collapsible.</summary>
    [JsonPropertyName("collapseDetailSections")]
    public bool CollapseDetailSections { get; set; }

    /// <summary>Gets or sets the Jellyfin item types that can be commented on.</summary>
    [JsonPropertyName("commentableTypes")]
    public List<string> CommentableTypes { get; set; } = new();

    /// <summary>Gets or sets the section heading.</summary>
    [JsonPropertyName("sectionTitle")]
    public string SectionTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the comment box placeholder.</summary>
    [JsonPropertyName("inputPlaceholder")]
    public string InputPlaceholder { get; set; } = string.Empty;

    /// <summary>Gets or sets the empty-list text.</summary>
    [JsonPropertyName("emptyText")]
    public string EmptyText { get; set; } = string.Empty;

    /// <summary>Gets or sets the spoiler placeholder text.</summary>
    [JsonPropertyName("spoilerText")]
    public string SpoilerText { get; set; } = string.Empty;
}
