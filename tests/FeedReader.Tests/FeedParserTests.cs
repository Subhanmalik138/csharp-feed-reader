using Xunit;
using FeedReader;

namespace FeedReader.Tests;

public sealed class FeedParserTests
{
    [Fact]
    public void ParsesRssChannelAndItems()
    {
        var feed = FeedParser.Parse(SampleFeeds.Rss);

        Assert.Equal("Example Blog", feed.Title);
        Assert.Equal("https://example.com", feed.SiteUrl);
        Assert.Equal(2, feed.Items.Count);
    }

    [Fact]
    public void RssItemFields_GuidTitleAuthorDate()
    {
        var item = FeedParser.Parse(SampleFeeds.Rss).Items[0];

        Assert.Equal("guid-1", item.Guid);
        Assert.Equal("First Post", item.Title);
        Assert.Equal("https://example.com/first", item.Link);
        Assert.Equal("Alice", item.Author);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero), item.PublishedAt);
    }

    [Fact]
    public void RssItem_GuidFallsBackToLink_WhenMissing()
    {
        var item = FeedParser.Parse(SampleFeeds.Rss).Items[1];

        Assert.Equal("https://example.com/second", item.Guid);
    }

    [Fact]
    public void StripsHtmlAndDecodesEntities_FromDescription()
    {
        var item = FeedParser.Parse(SampleFeeds.Rss).Items[0];

        Assert.Equal("Hello world & friends.", item.Description);
        Assert.DoesNotContain("<", item.Description);
    }

    [Fact]
    public void ParsesAtomEntries_WithAlternateLinks()
    {
        var feed = FeedParser.Parse(SampleFeeds.Atom);

        Assert.Equal("Atom Journal", feed.Title);
        Assert.Equal("https://atom.example.com/", feed.SiteUrl);
        Assert.Equal(2, feed.Items.Count);

        var first = feed.Items[0];
        Assert.Equal("urn:atom:1", first.Guid);
        Assert.Equal("https://atom.example.com/one", first.Link);
        Assert.Equal("Bob", first.Author);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero), first.PublishedAt);

        // Second entry has no rel="alternate": the bare link still wins.
        Assert.Equal("https://atom.example.com/two", feed.Items[1].Link);
        // content wins over summary and is stripped to plain text.
        Assert.Equal("With markup.", feed.Items[1].Description);
    }

    [Fact]
    public void MalformedXml_ThrowsFeedParseException()
    {
        Assert.Throws<FeedParseException>(() => FeedParser.Parse("<rss><channel>"));
    }

    [Fact]
    public void UnsupportedRoot_ThrowsFeedParseException()
    {
        Assert.Throws<FeedParseException>(() => FeedParser.Parse("<html><body>nope</body></html>"));
    }

    [Theory]
    [InlineData("Mon, 09 Nov 2026 10:00:00 GMT", 2026, 11, 9, 10, 0)]
    [InlineData("2026-11-09T12:00:00Z", 2026, 11, 9, 12, 0)]
    [InlineData("2026-11-09", 2026, 11, 9, 0, 0)]
    public void ParsesCommonDateFormats(string raw, int y, int m, int d, int hh, int mm)
    {
        var parsed = FeedParser.ParseDate(raw);

        Assert.NotNull(parsed);
        Assert.Equal(y, parsed!.Value.Year);
        Assert.Equal(m, parsed.Value.Month);
        Assert.Equal(d, parsed.Value.Day);
        Assert.Equal(hh, parsed.Value.Hour);
        Assert.Equal(mm, parsed.Value.Minute);
    }

    [Fact]
    public void UnparseableDate_Null_ReturnsNull()
    {
        Assert.Null(FeedParser.ParseDate(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a date")]
    public void UnparseableDate_ReturnsNull(string raw)
    {
        Assert.Null(FeedParser.ParseDate(raw));
    }
}
