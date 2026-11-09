namespace FeedReader;

/// <summary>Temporary entry point; replaced by the real command dispatcher in the CLI stage.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("feedreader 1.0.0 — CLI RSS/Atom feed aggregator");
        Console.WriteLine("Run `feedreader --help` for commands.");
        return 0;
    }
}
