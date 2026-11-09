namespace FeedReader;

/// <summary>Resolves where the database lives; kept separate so tests can point it at a temp file.</summary>
public static class Paths
{
    public static string DefaultDataPath() => JsonFeedStore.DefaultDataPath();
}
