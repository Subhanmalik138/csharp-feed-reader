using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FeedReader;

/// <summary>One parsed entry, before it is stored as a <see cref="FeedItem"/>.</summary>
public sealed class ParsedItem
{
    public required string Guid { get; init; }
    public required string Title { get; init; }
    public required string Link { get; init; }
    public string? Description { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>The parsed feed document: its metadata plus its entries.</summary>
public sealed class ParsedFeed
{
    public string? Title { get; init; }
    public string? SiteUrl { get; init; }
    public required List<ParsedItem> Items { get; init; }
}

public sealed class FeedParseException : Exception
{
    public FeedParseException(string message) : base(message) { }
    public FeedParseException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Parses RSS 2.0 and Atom 1.0 documents. Matching is done on local element
/// names so feeds that use (or omit) namespaces, prefixes and extensions
/// like Dublin Core all parse the same way.
/// </summary>
public static class FeedParser
{
    private static readonly string[] DateFormats =
    {
        "r", // RFC 1123, e.g. Mon, 09 Nov 2026 10:00:00 GMT
        "ddd, dd MMM yyyy HH:mm:ss zzz",
        "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
        "yyyy-MM-ddTHH:mm:sszzz",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd"
    };

    public static ParsedFeed Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            throw new FeedParseException("Feed is not well-formed XML.", ex);
        }

        var root = doc.Root ?? throw new FeedParseException("Feed document has no root element.");

        return root.Name.LocalName switch
        {
            "rss" => ParseRss(root),
            "feed" => ParseAtom(root),
            _ => throw new FeedParseException($"Unsupported feed format: root element <{root.Name.LocalName}>. Expected <rss> or <feed>.")
        };
    }

    private static ParsedFeed ParseRss(XElement rss)
    {
        var channel = rss.Element("channel")
            ?? throw new FeedParseException("RSS document has no <channel> element.");

        var items = channel.Elements("item").Select(ParseRssItem).ToList();
        return new ParsedFeed
        {
            Title = CleanText(ChildText(channel, "title")),
            SiteUrl = CleanText(ChildText(channel, "link")),
            Items = items
        };
    }

    private static ParsedItem ParseRssItem(XElement item)
    {
        var link = CleanText(ChildText(item, "link")) ?? "";
        var guid = CleanText(ChildText(item, "guid")) ?? link;
        var rawDescription = ChildText(item, "description");
        var content = ChildText(item, "encoded"); // content:encoded, matched by local name

        return new ParsedItem
        {
            Guid = string.IsNullOrWhiteSpace(guid) ? link : guid,
            Title = CleanText(ChildText(item, "title")) ?? "(untitled)",
            Link = link,
            Description = StripHtml(content ?? rawDescription),
            Author = CleanText(ChildText(item, "creator") ?? ChildText(item, "author")),
            PublishedAt = ParseDate(ChildText(item, "pubDate") ?? ChildText(item, "date"))
        };
    }

    private static ParsedFeed ParseAtom(XElement feed)
    {
        var items = feed.Elements().Where(e => e.Name.LocalName == "entry").Select(ParseAtomEntry).ToList();
        return new ParsedFeed
        {
            Title = CleanText(ChildText(feed, "title")),
            SiteUrl = AtomLink(feed, "alternate") ?? AtomLink(feed, null),
            Items = items
        };
    }

    private static ParsedItem ParseAtomEntry(XElement entry)
    {
        var link = AtomLink(entry, "alternate") ?? AtomLink(entry, null) ?? "";
        var id = CleanText(ChildText(entry, "id")) ?? link;
        var summary = ChildText(entry, "summary");
        var content = ChildText(entry, "content");

        return new ParsedItem
        {
            Guid = string.IsNullOrWhiteSpace(id) ? link : id,
            Title = CleanText(ChildText(entry, "title")) ?? "(untitled)",
            Link = link,
            Description = StripHtml(content ?? summary),
            Author = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "author")
                ?.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value.Trim(),
            PublishedAt = ParseDate(ChildText(entry, "published") ?? ChildText(entry, "updated"))
        };
    }

    /// <summary>Atom links are chosen by rel; without a rel, the first link wins.</summary>
    private static string? AtomLink(XElement parent, string? rel)
    {
        foreach (var link in parent.Elements().Where(e => e.Name.LocalName == "link"))
        {
            var linkRel = link.Attribute("rel")?.Value ?? "alternate";
            var href = link.Attribute("href")?.Value;
            if (href is null)
            {
                continue;
            }
            if (rel is null || linkRel == rel)
            {
                return href.Trim();
            }
        }
        return null;
    }

    private static string? ChildText(XElement parent, string localName)
    {
        var value = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var format in DateFormats)
        {
            if (DateTimeOffset.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                return parsed;
            }
        }

        return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var fallback) ? fallback : null;
    }

    /// <summary>Strips HTML tags, decodes entities and collapses whitespace; caps length for storage.</summary>
    internal static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var collapsed = Regex.Replace(decoded, @"\s+", " ").Trim();
        return collapsed.Length == 0 ? null : collapsed[..Math.Min(collapsed.Length, 2000)];
    }

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var cleaned = Regex.Replace(value.Trim(), @"\s+", " ");
        return cleaned.Length == 0 ? null : cleaned;
    }
}
