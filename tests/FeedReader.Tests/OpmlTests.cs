using Xunit;
using FeedReader;

namespace FeedReader.Tests;

public sealed class OpmlTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "feedreader-opml-" + Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => Path.Combine(_dir, name);

    public OpmlTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private const string OpmlSample = """
        <?xml version="1.0" encoding="UTF-8"?>
        <opml version="2.0">
          <head><title>Subscriptions</title></head>
          <body>
            <outline text="Tech" title="Tech">
              <outline type="rss" text="Example Blog" title="Example Blog" xmlUrl="https://example.com/feed.xml" htmlUrl="https://example.com"/>
            </outline>
            <outline type="rss" text="Atom Journal" title="Atom Journal" xmlUrl="https://atom.example.com/feed.xml"/>
          </body>
        </opml>
        """;

    [Fact]
    public void Import_AddsFeedsFromNestedOutlines_AndSkipsDuplicates()
    {
        var opmlPath = PathFor("subs.opml");
        File.WriteAllText(opmlPath, OpmlSample);
        var store = new JsonFeedStore(PathFor("data.json"));

        var first = OpmlCommands.Import(store, new[] { opmlPath });
        var second = OpmlCommands.Import(store, new[] { opmlPath });
        var db = store.Load();

        Assert.Equal(0, first);
        Assert.Equal(0, second);
        Assert.Equal(2, db.Feeds.Count);
        var tech = db.Feeds.Single(f => f.Url == "https://example.com/feed.xml");
        Assert.Equal("Example Blog", tech.Title);
        Assert.Equal("Tech", tech.Category); // folder name becomes the category
    }

    [Fact]
    public void ExportThenImport_RoundTripsSubscriptions()
    {
        var store = new JsonFeedStore(PathFor("data.json"));
        var db = new FeedDatabase
        {
            Feeds =
            {
                new Feed { Title = "One", Url = "https://one.example/feed", SiteUrl = "https://one.example", Category = "news" },
                new Feed { Title = "Two", Url = "https://two.example/feed" }
            }
        };
        store.Save(db);

        var opmlPath = PathFor("out.opml");
        OpmlCommands.Export(store, new[] { opmlPath });

        var fresh = new JsonFeedStore(PathFor("fresh.json"));
        OpmlCommands.Import(fresh, new[] { opmlPath });
        var imported = fresh.Load();

        Assert.Equal(2, imported.Feeds.Count);
        Assert.Contains(imported.Feeds, f => f.Url == "https://one.example/feed" && f.Category == "news");
        Assert.Contains(imported.Feeds, f => f.Url == "https://two.example/feed");
    }

    [Fact]
    public void Import_MissingFile_ThrowsArgumentException()
    {
        var store = new JsonFeedStore(PathFor("data.json"));
        Assert.Throws<ArgumentException>(() => OpmlCommands.Import(store, new[] { PathFor("nope.opml") }));
    }
}
