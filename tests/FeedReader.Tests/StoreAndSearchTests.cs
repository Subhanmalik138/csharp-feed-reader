using Xunit;
using FeedReader;

namespace FeedReader.Tests;

public sealed class StoreAndSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "feedreader-tests-" + Guid.NewGuid().ToString("N"));
    private string DataPath => Path.Combine(_dir, "sub", "data.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private FeedDatabase SeededDb()
    {
        var feedA = new Feed { Id = "feedaaaa", Title = "Tech Daily", Url = "https://tech.example/feed" };
        var feedB = new Feed { Id = "feedbbbb", Title = "Cooking Weekly", Url = "https://cook.example/feed" };
        return new FeedDatabase
        {
            Feeds = { feedA, feedB },
            Items =
            {
                new FeedItem { Id = "item0001", FeedId = "feedaaaa", Guid = "g1", Title = "C# 13 Released",
                    Description = "Records and collections get nicer.", Author = "Alice",
                    PublishedAt = new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero) },
                new FeedItem { Id = "item0002", FeedId = "feedaaaa", Guid = "g2", Title = "Rust vs Go",
                    Description = "A benchmark shootout.", IsRead = true,
                    PublishedAt = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero) },
                new FeedItem { Id = "item0003", FeedId = "feedbbbb", Guid = "g3", Title = "Sourdough Basics",
                    Description = "C# not required, flour is.", IsStarred = true,
                    PublishedAt = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero) }
            }
        };
    }

    [Fact]
    public void MissingFile_LoadsEmptyDatabase()
    {
        var db = new JsonFeedStore(DataPath).Load();

        Assert.Empty(db.Feeds);
        Assert.Empty(db.Items);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_AllData()
    {
        var store = new JsonFeedStore(DataPath);
        var original = SeededDb();

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Feeds.Count);
        Assert.Equal(3, loaded.Items.Count);
        Assert.Equal("C# 13 Released", loaded.Items[0].Title);
        Assert.True(loaded.Items[1].IsRead);
        Assert.True(loaded.Items[2].IsStarred);
        // Save created the nested directory.
        Assert.True(File.Exists(DataPath));
    }

    [Fact]
    public void Search_MatchesTitleDescriptionAndAuthor_CaseInsensitive()
    {
        var db = SeededDb();

        Assert.Single(FeedSearch.Query(db, new ItemQuery { Text = "c# 13" }));
        Assert.Single(FeedSearch.Query(db, new ItemQuery { Text = "benchmark" }));
        Assert.Single(FeedSearch.Query(db, new ItemQuery { Text = "alice" }));
        Assert.Equal(2, FeedSearch.Query(db, new ItemQuery { Text = "c#", Limit = 10 }).Count());
    }

    [Fact]
    public void Query_Filters_ByFeedUnreadStarredAndSince()
    {
        var db = SeededDb();

        Assert.Equal(2, FeedSearch.Query(db, new ItemQuery { FeedId = "feedaaaa", Limit = 10 }).Count());
        Assert.Equal(2, FeedSearch.Query(db, new ItemQuery { UnreadOnly = true, Limit = 10 }).Count());
        Assert.Single(FeedSearch.Query(db, new ItemQuery { StarredOnly = true }));
        Assert.Single(FeedSearch.Query(db, new ItemQuery
        {
            Since = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero),
            Limit = 10
        }));
    }

    [Fact]
    public void Query_OrdersNewestFirst_AndRespectsLimit()
    {
        var db = SeededDb();

        var results = FeedSearch.Query(db, new ItemQuery { Limit = 2 }).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("item0001", results[0].Id);
        Assert.Equal("item0002", results[1].Id);
    }

    [Fact]
    public void UnreadCount_ScopedPerFeed()
    {
        var db = SeededDb();

        Assert.Equal(2, FeedSearch.UnreadCount(db));
        Assert.Equal(1, FeedSearch.UnreadCount(db, "feedaaaa"));
        Assert.Equal(1, FeedSearch.UnreadCount(db, "feedbbbb"));
    }
}
