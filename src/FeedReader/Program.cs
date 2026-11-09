namespace FeedReader;

/// <summary>
/// feedreader — a CLI RSS/Atom feed aggregator.
/// Data lives in a single JSON file (see --data / FEEDREADER_DATA).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var (dataPath, rest) = ExtractDataOption(args);
        var store = new JsonFeedStore(dataPath);

        if (rest.Length == 0 || rest[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var command = rest[0];
        var cmdArgs = rest[1..];

        try
        {
            return command switch
            {
                "add" => Add(store, cmdArgs),
                "remove" => Remove(store, cmdArgs),
                "feeds" => ListFeeds(store, cmdArgs),
                "refresh" => await Refresh(store, cmdArgs),
                "list" => ListItems(store, cmdArgs),
                "show" => Show(store, cmdArgs),
                "read" => SetRead(store, cmdArgs, read: true),
                "unread" => SetRead(store, cmdArgs, read: false),
                "star" => SetStarred(store, cmdArgs, starred: true),
                "unstar" => SetStarred(store, cmdArgs, starred: false),
                "search" => Search(store, cmdArgs),
                "stats" => Stats(store),
                _ => Unknown(command)
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            feedreader — CLI RSS/Atom feed aggregator

            Usage: feedreader [--data <path>] <command> [options]

            Feed management:
              add <url> [--title T] [--category C]   Subscribe to a feed
              remove <id|url>                         Unsubscribe (deletes its items too)
              feeds [--category C]                    List subscribed feeds
              refresh [id|url]                        Fetch new items (all feeds if omitted)

            Reading:
              list [--feed ID] [--unread] [--starred] [--limit N] [--since DATE]
              show <item-id>                          Show one item in full
              read <item-id> | unread <item-id>      Mark an item read / unread
              star <item-id> | unstar <item-id>      Star / unstar an item
              search <query> [--feed ID] [--unread] [--limit N]
              stats                                   Totals and per-feed status

            Import / export:
              import-opml <file>                      Subscribe to every feed in an OPML file
              export-opml [file]                      Write subscriptions as OPML (stdout if omitted)
              export [--format json|csv] [--feed ID] [--unread] [--output FILE]

            Data file: ~/.local/share/feedreader/data.json (override with --data or FEEDREADER_DATA)
            """);
    }

    // -- Commands -----------------------------------------------------------

    private static int Add(JsonFeedStore store, string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Usage: feedreader add <url> [--title T] [--category C]");
        }

        var url = args[0];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new ArgumentException($"Not a valid http(s) feed URL: {url}");
        }

        var db = store.Load();
        if (db.Feeds.Any(f => string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Already subscribed to that feed.");
            return 0;
        }

        var feed = new Feed
        {
            Url = url,
            Title = Option(args, "--title") ?? url,
            Category = Option(args, "--category")
        };
        db.Feeds.Add(feed);
        store.Save(db);
        Console.WriteLine($"Subscribed: [{feed.Id}] {feed.Title}");
        Console.WriteLine("Run `feedreader refresh` to fetch its items.");
        return 0;
    }

    private static int Remove(JsonFeedStore store, string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Usage: feedreader remove <id|url>");
        }

        var db = store.Load();
        var feed = FindFeed(db, args[0]);
        db.Feeds.Remove(feed);
        var removedItems = db.Items.RemoveAll(i => i.FeedId == feed.Id);
        store.Save(db);
        Console.WriteLine($"Removed [{feed.Id}] {feed.Title} ({removedItems} items deleted).");
        return 0;
    }

    private static int ListFeeds(JsonFeedStore store, string[] args)
    {
        var db = store.Load();
        var category = Option(args, "--category");
        var feeds = db.Feeds.AsEnumerable();
        if (category is not null)
        {
            feeds = feeds.Where(f => string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        var list = feeds.OrderBy(f => f.Title).ToList();
        if (list.Count == 0)
        {
            Console.WriteLine("No feeds subscribed yet. Add one with `feedreader add <url>`.");
            return 0;
        }

        Console.WriteLine($"{"ID",-10} {"ITEMS",5} {"UNREAD",6}  TITLE");
        foreach (var feed in list)
        {
            var total = db.Items.Count(i => i.FeedId == feed.Id);
            var unread = FeedSearch.UnreadCount(db, feed.Id);
            var status = feed.LastError is null ? "" : "  ⚠ " + feed.LastError;
            Console.WriteLine($"{feed.Id,-10} {total,5} {unread,6}  {feed.Title}{status}");
            Console.WriteLine($"{"",-10} {"",5} {"",6}  {feed.Url}");
        }
        return 0;
    }

    private static async Task<int> Refresh(JsonFeedStore store, string[] args)
    {
        var db = store.Load();
        var targets = db.Feeds.AsEnumerable();
        if (args.Length > 0)
        {
            targets = new[] { FindFeed(db, args[0]) };
        }

        var list = targets.ToList();
        if (list.Count == 0)
        {
            Console.WriteLine("No feeds subscribed yet. Add one with `feedreader add <url>`.");
            return 0;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var fetcher = new FeedFetcher(http);
        var totalNew = 0;
        foreach (var feed in list)
        {
            var result = await fetcher.FetchAsync(feed, db);
            if (result.NotModified)
            {
                Console.WriteLine($"[{feed.Id}] {feed.Title}: no changes");
            }
            else if (result.Success)
            {
                Console.WriteLine($"[{feed.Id}] {feed.Title}: +{result.NewItems} new item(s)");
                totalNew += result.NewItems;
            }
            else
            {
                Console.WriteLine($"[{feed.Id}] {feed.Title}: FAILED — {result.Error}");
            }
        }

        store.Save(db);
        Console.WriteLine($"Done. {totalNew} new item(s) across {list.Count} feed(s).");
        return 0;
    }

    private static int ListItems(JsonFeedStore store, string[] args)
    {
        var db = store.Load();
        var query = BuildQuery(args);
        PrintItems(db, FeedSearch.Query(db, query));
        return 0;
    }

    private static int Show(JsonFeedStore store, string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Usage: feedreader show <item-id>");
        }

        var db = store.Load();
        var item = FindItem(db, args[0]);
        var feed = db.Feeds.FirstOrDefault(f => f.Id == item.FeedId);

        Console.WriteLine(item.Title);
        Console.WriteLine(new string('=', Math.Max(item.Title.Length, 1)));
        Console.WriteLine($"Feed:      {feed?.Title ?? item.FeedId}");
        Console.WriteLine($"Published: {item.PublishedAt?.ToString("yyyy-MM-dd HH:mm") ?? "unknown"}");
        Console.WriteLine($"Author:    {item.Author ?? "—"}");
        Console.WriteLine($"Link:      {item.Link}");
        Console.WriteLine($"Status:    {(item.IsRead ? "read" : "unread")}{(item.IsStarred ? " ★ starred" : "")}");
        Console.WriteLine();
        Console.WriteLine(string.IsNullOrWhiteSpace(item.Description) ? "(no description)" : item.Description);
        return 0;
    }

    private static int SetRead(JsonFeedStore store, string[] args, bool read)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Usage: feedreader read|unread <item-id>");
        }

        var db = store.Load();
        var item = FindItem(db, args[0]);
        item.IsRead = read;
        store.Save(db);
        Console.WriteLine($"Marked {(read ? "read" : "unread")}: {item.Title}");
        return 0;
    }

    private static int SetStarred(JsonFeedStore store, string[] args, bool starred)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Usage: feedreader star|unstar <item-id>");
        }

        var db = store.Load();
        var item = FindItem(db, args[0]);
        item.IsStarred = starred;
        store.Save(db);
        Console.WriteLine($"{(starred ? "Starred" : "Unstarred")}: {item.Title}");
        return 0;
    }

    private static int Search(JsonFeedStore store, string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Usage: feedreader search <query> [--feed ID] [--unread] [--limit N]");
        }

        var db = store.Load();
        var query = BuildQuery(args[1..]) with { Text = args[0] };
        var results = FeedSearch.Query(db, query).ToList();
        Console.WriteLine($"{results.Count} result(s) for \"{args[0]}\":");
        PrintItems(db, results);
        return 0;
    }

    private static int Stats(JsonFeedStore store)
    {
        var db = store.Load();
        Console.WriteLine($"Feeds:   {db.Feeds.Count}");
        Console.WriteLine($"Items:   {db.Items.Count}");
        Console.WriteLine($"Unread:  {FeedSearch.UnreadCount(db)}");
        Console.WriteLine($"Starred: {db.Items.Count(i => i.IsStarred)}");
        Console.WriteLine();

        foreach (var feed in db.Feeds.OrderByDescending(f => FeedSearch.UnreadCount(db, f.Id)))
        {
            var total = db.Items.Count(i => i.FeedId == feed.Id);
            var unread = FeedSearch.UnreadCount(db, feed.Id);
            var fetched = feed.LastFetchedAt?.ToString("yyyy-MM-dd HH:mm") ?? "never";
            var error = feed.LastError is null ? "" : $"  ⚠ {feed.LastError}";
            Console.WriteLine($"[{feed.Id}] {feed.Title}: {total} items, {unread} unread, last fetch {fetched}{error}");
        }
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine("Run `feedreader --help` for the command list.");
        return 1;
    }

    // -- Helpers ------------------------------------------------------------

    private static (string? DataPath, string[] Rest) ExtractDataOption(string[] args)
    {
        var rest = new List<string>();
        string? dataPath = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data" && i + 1 < args.Length)
            {
                dataPath = args[++i];
            }
            else
            {
                rest.Add(args[i]);
            }
        }
        return (dataPath, rest.ToArray());
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

    private static bool HasFlag(string[] args, string name) => args.Contains(name);

    private static int IntOption(string[] args, string name, int fallback)
    {
        var raw = Option(args, name);
        return raw is not null && int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static ItemQuery BuildQuery(string[] args)
    {
        DateTimeOffset? since = null;
        var sinceRaw = Option(args, "--since");
        if (sinceRaw is not null && !DateTimeOffset.TryParse(sinceRaw, out var parsed))
        {
            throw new ArgumentException($"Cannot parse --since date: {sinceRaw}");
        }
        else if (sinceRaw is not null)
        {
            since = parsed;
        }

        return new ItemQuery
        {
            FeedId = Option(args, "--feed"),
            UnreadOnly = HasFlag(args, "--unread"),
            StarredOnly = HasFlag(args, "--starred"),
            Since = since,
            Limit = IntOption(args, "--limit", 20)
        };
    }

    private static Feed FindFeed(FeedDatabase db, string idOrUrl)
    {
        var feed = db.Feeds.FirstOrDefault(f =>
            f.Id == idOrUrl ||
            f.Id.StartsWith(idOrUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.Url, idOrUrl, StringComparison.OrdinalIgnoreCase));
        return feed ?? throw new InvalidOperationException($"No subscribed feed matches '{idOrUrl}'.");
    }

    private static FeedItem FindItem(FeedDatabase db, string id)
    {
        var item = db.Items.FirstOrDefault(i =>
            i.Id == id || i.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
        return item ?? throw new InvalidOperationException($"No item matches id '{id}'.");
    }

    private static void PrintItems(FeedDatabase db, IEnumerable<FeedItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            Console.WriteLine("No items. Try `feedreader refresh` first.");
            return;
        }

        var feedTitles = db.Feeds.ToDictionary(f => f.Id, f => f.Title);
        foreach (var item in list)
        {
            var date = (item.PublishedAt ?? item.FetchedAt).ToString("yyyy-MM-dd");
            var mark = item.IsRead ? " " : "•";
            var star = item.IsStarred ? " ★" : "";
            var feedTitle = feedTitles.TryGetValue(item.FeedId, out var t) ? t : item.FeedId;
            Console.WriteLine($"[{item.Id}] {mark} {date} {item.Title}{star}");
            Console.WriteLine($"        {feedTitle} — {item.Link}");
        }
    }
}
