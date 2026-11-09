using System.Text;
using System.Text.Json;

namespace FeedReader;

/// <summary>Exports matching items as JSON or CSV, for analysis elsewhere.</summary>
public static class ItemExporter
{
    public static int Export(JsonFeedStore store, string[] args)
    {
        var format = Option(args, "--format")?.ToLowerInvariant() ?? "json";
        if (format is not ("json" or "csv"))
        {
            throw new ArgumentException($"Unsupported export format: {format}. Use json or csv.");
        }

        var db = store.Load();
        var query = BuildExportQuery(args);
        var items = FeedSearch.Query(db, query).ToList();
        var feedTitles = db.Feeds.ToDictionary(f => f.Id, f => f.Title);

        var output = format == "json" ? ToJson(items, feedTitles) : ToCsv(items, feedTitles);

        var outputPath = Option(args, "--output");
        if (outputPath is null)
        {
            Console.Write(output);
        }
        else
        {
            File.WriteAllText(outputPath, output);
            Console.WriteLine($"Exported {items.Count} item(s) as {format} to {outputPath}");
        }
        return 0;

        static ItemQuery BuildExportQuery(string[] args)
        {
            DateTimeOffset? since = null;
            var sinceRaw = Option(args, "--since");
            if (sinceRaw is not null)
            {
                if (!DateTimeOffset.TryParse(sinceRaw, out var parsed))
                {
                    throw new ArgumentException($"Cannot parse --since date: {sinceRaw}");
                }
                since = parsed;
            }

            return new ItemQuery
            {
                FeedId = Option(args, "--feed"),
                UnreadOnly = args.Contains("--unread"),
                StarredOnly = args.Contains("--starred"),
                Since = since,
                Limit = int.MaxValue // export is unbounded; list/search keep the default page size
            };
        }
    }

    private static string ToJson(List<FeedItem> items, Dictionary<string, string> feedTitles)
    {
        var rows = items.Select(i => new
        {
            id = i.Id,
            feed = feedTitles.TryGetValue(i.FeedId, out var t) ? t : i.FeedId,
            title = i.Title,
            link = i.Link,
            description = i.Description,
            author = i.Author,
            publishedAt = i.PublishedAt?.ToString("o"),
            isRead = i.IsRead,
            isStarred = i.IsStarred
        });
        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ToCsv(List<FeedItem> items, Dictionary<string, string> feedTitles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("id,feed,title,link,author,published_at,is_read,is_starred");
        foreach (var item in items)
        {
            var feed = feedTitles.TryGetValue(item.FeedId, out var t) ? t : item.FeedId;
            sb.AppendLine(string.Join(",",
                Csv(item.Id), Csv(feed), Csv(item.Title), Csv(item.Link), Csv(item.Author ?? ""),
                Csv(item.PublishedAt?.ToString("o") ?? ""),
                item.IsRead ? "true" : "false",
                item.IsStarred ? "true" : "false"));
        }
        return sb.ToString();

        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string? Option(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
