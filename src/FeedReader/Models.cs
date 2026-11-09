namespace FeedReader;

/// <summary>A subscribed feed: its source URL plus fetch bookkeeping.</summary>
public sealed class Feed
{
    /// <summary>Short stable id used on the command line (8 hex chars).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Display title. Defaults to the URL until the first fetch fills it in from the feed.</summary>
    public string Title { get; set; } = "";

    /// <summary>The RSS/Atom document URL.</summary>
    public string Url { get; set; } = "";

    /// <summary>The feed's home page, taken from the feed document when present.</summary>
    public string? SiteUrl { get; set; }

    /// <summary>Optional user grouping, e.g. "tech" or "news".</summary>
    public string? Category { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastFetchedAt { get; set; }

    /// <summary>Last fetch failure, if any; cleared on the next successful fetch.</summary>
    public string? LastError { get; set; }

    public int FetchCount { get; set; }

    /// <summary>Validators for conditional GETs, so unchanged feeds cost one 304.</summary>
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
}

/// <summary>One entry from a feed. Identity is (FeedId, Guid) so refreshes never duplicate items.</summary>
public sealed class FeedItem
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N")[..8];
    public string FeedId { get; set; } = "";

    /// <summary>Dedupe key: the feed's guid, falling back to the item link.</summary>
    public string Guid { get; set; } = "";

    public string Title { get; set; } = "";
    public string Link { get; set; } = "";

    /// <summary>Plain-text summary; HTML from the feed is stripped at parse time.</summary>
    public string? Description { get; set; }

    public string? Author { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public bool IsRead { get; set; }
    public bool IsStarred { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>The whole on-disk state: feeds plus every fetched item.</summary>
public sealed class FeedDatabase
{
    public List<Feed> Feeds { get; set; } = new();
    public List<FeedItem> Items { get; set; } = new();
}
