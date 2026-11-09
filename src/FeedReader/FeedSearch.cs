namespace FeedReader;

/// <summary>Filter options for listing and searching items.</summary>
public sealed class ItemQuery
{
    public string? FeedId { get; init; }
    public bool UnreadOnly { get; init; }
    public bool StarredOnly { get; init; }
    public string? Text { get; init; }
    public DateTimeOffset? Since { get; init; }
    public int Limit { get; init; } = 20;
}

/// <summary>
/// Read-side queries over the database. Search is a case-insensitive
/// substring match across title, description and author — simple, honest
/// full-text search with no index to maintain.
/// </summary>
public static class FeedSearch
{
    public static IEnumerable<FeedItem> Query(FeedDatabase db, ItemQuery query)
    {
        IEnumerable<FeedItem> items = db.Items;

        if (query.FeedId is not null)
        {
            items = items.Where(i => i.FeedId == query.FeedId);
        }
        if (query.UnreadOnly)
        {
            items = items.Where(i => !i.IsRead);
        }
        if (query.StarredOnly)
        {
            items = items.Where(i => i.IsStarred);
        }
        if (query.Since is not null)
        {
            items = items.Where(i => (i.PublishedAt ?? i.FetchedAt) >= query.Since.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var needle = query.Text.Trim();
            items = items.Where(i =>
                (i.Title?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Description?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Author?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Newest first; items without a publish date sort by when they were fetched.
        return items
            .OrderByDescending(i => i.PublishedAt ?? i.FetchedAt)
            .Take(query.Limit);
    }

    public static int UnreadCount(FeedDatabase db, string? feedId = null) =>
        db.Items.Count(i => !i.IsRead && (feedId is null || i.FeedId == feedId));
}
