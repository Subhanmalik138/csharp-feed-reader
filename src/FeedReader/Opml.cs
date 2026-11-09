using System.Xml.Linq;

namespace FeedReader;

/// <summary>
/// OPML 2.0 import/export for subscriptions, so a feed list can move
/// between FeedReader and any other reader.
/// </summary>
public static class OpmlCommands
{
    public static int Import(JsonFeedStore store, string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Usage: feedreader import-opml <file>");
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            throw new ArgumentException($"OPML file not found: {path}");
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(path);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Cannot parse OPML file: {ex.Message}");
        }

        // Every <outline> with an xmlUrl is a feed, at any nesting depth
        // (folders are just outlines without xmlUrl).
        var outlines = doc.Descendants("outline")
            .Where(o => o.Attribute("xmlUrl") is not null)
            .Select(o => new
            {
                Url = o.Attribute("xmlUrl")!.Value.Trim(),
                Title = o.Attribute("title")?.Value?.Trim() ?? o.Attribute("text")?.Value?.Trim(),
                Category = o.Ancestors("outline")
                    .Select(a => a.Attribute("text")?.Value)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
            })
            .Where(o => o.Url.Length > 0)
            .ToList();

        var db = store.Load();
        var added = 0;
        var skipped = 0;
        foreach (var outline in outlines)
        {
            if (db.Feeds.Any(f => string.Equals(f.Url, outline.Url, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            db.Feeds.Add(new Feed
            {
                Url = outline.Url,
                Title = string.IsNullOrWhiteSpace(outline.Title) ? outline.Url : outline.Title,
                Category = outline.Category
            });
            added++;
        }

        store.Save(db);
        Console.WriteLine($"Imported {added} feed(s), skipped {skipped} already-subscribed.");
        return 0;
    }

    public static int Export(JsonFeedStore store, string[] args)
    {
        var db = store.Load();
        var doc = new XDocument(
            new XElement("opml",
                new XAttribute("version", "2.0"),
                new XElement("head",
                    new XElement("title", "FeedReader subscriptions"),
                    new XElement("dateCreated", DateTimeOffset.UtcNow.ToString("r"))),
                new XElement("body",
                    db.Feeds
                        .OrderBy(f => f.Category ?? "")
                        .ThenBy(f => f.Title)
                        .Select(f => new XElement("outline",
                            new XAttribute("type", "rss"),
                            new XAttribute("text", f.Title),
                            new XAttribute("title", f.Title),
                            new XAttribute("xmlUrl", f.Url),
                            f.SiteUrl is null ? null : new XAttribute("htmlUrl", f.SiteUrl),
                            f.Category is null ? null : new XAttribute("category", f.Category))))));

        var xml = doc.ToString();
        if (args.Length == 0)
        {
            Console.WriteLine(xml);
        }
        else
        {
            File.WriteAllText(args[0], xml);
            Console.WriteLine($"Wrote {db.Feeds.Count} feed(s) to {args[0]}");
        }
        return 0;
    }
}
