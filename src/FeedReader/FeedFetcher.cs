using System.Net;

namespace FeedReader;

/// <summary>Outcome of one feed refresh.</summary>
public sealed class FetchResult
{
    public required bool Success { get; init; }
    public bool NotModified { get; init; }
    public int NewItems { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Downloads a feed document and merges its entries into the database.
/// Uses conditional GETs (ETag / Last-Modified) so unchanged feeds cost a
/// single 304, and never duplicates an item: identity is (FeedId, Guid).
/// </summary>
public sealed class FeedFetcher
{
    private readonly HttpClient _http;

    public FeedFetcher(HttpClient http)
    {
        _http = http;
    }

    public async Task<FetchResult> FetchAsync(Feed feed, FeedDatabase db, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
        request.Headers.UserAgent.ParseAdd("FeedReader/1.0 (+https://github.com/Subhanmalik138/csharp-feed-reader)");
        if (feed.ETag is not null)
        {
            request.Headers.IfNoneMatch.ParseAdd(feed.ETag);
        }
        if (feed.LastModified is not null && DateTimeOffset.TryParse(feed.LastModified, out var lastModified))
        {
            request.Headers.IfModifiedSince = lastModified;
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Fail(feed, $"Network error: {ex.Message}");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                feed.LastFetchedAt = DateTimeOffset.UtcNow;
                feed.FetchCount++;
                feed.LastError = null;
                return new FetchResult { Success = true, NotModified = true };
            }

            if (!response.IsSuccessStatusCode)
            {
                return Fail(feed, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            string xml;
            try
            {
                xml = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                return Fail(feed, $"Failed to read response: {ex.Message}");
            }

            ParsedFeed parsed;
            try
            {
                parsed = FeedParser.Parse(xml);
            }
            catch (FeedParseException ex)
            {
                return Fail(feed, ex.Message);
            }

            if (!string.IsNullOrWhiteSpace(parsed.Title))
            {
                feed.Title = parsed.Title;
            }
            if (!string.IsNullOrWhiteSpace(parsed.SiteUrl))
            {
                feed.SiteUrl = parsed.SiteUrl;
            }

            feed.ETag = response.Headers.ETag?.Tag;
            feed.LastModified = response.Content.Headers.LastModified?.ToString("r");
            feed.LastFetchedAt = DateTimeOffset.UtcNow;
            feed.FetchCount++;
            feed.LastError = null;

            var knownGuids = db.Items
                .Where(i => i.FeedId == feed.Id)
                .Select(i => i.Guid)
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var parsedItem in parsed.Items)
            {
                if (string.IsNullOrWhiteSpace(parsedItem.Guid) || knownGuids.Contains(parsedItem.Guid))
                {
                    continue;
                }

                db.Items.Add(new FeedItem
                {
                    FeedId = feed.Id,
                    Guid = parsedItem.Guid,
                    Title = parsedItem.Title,
                    Link = parsedItem.Link,
                    Description = parsedItem.Description,
                    Author = parsedItem.Author,
                    PublishedAt = parsedItem.PublishedAt
                });
                knownGuids.Add(parsedItem.Guid);
                added++;
            }

            return new FetchResult { Success = true, NewItems = added };
        }
    }

    private static FetchResult Fail(Feed feed, string error)
    {
        feed.LastError = error;
        feed.LastFetchedAt = DateTimeOffset.UtcNow;
        return new FetchResult { Success = false, Error = error };
    }
}
