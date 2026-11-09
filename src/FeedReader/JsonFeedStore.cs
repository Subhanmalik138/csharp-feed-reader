using System.Text.Json;

namespace FeedReader;

/// <summary>
/// Persistence for the whole feed database: one JSON file, written atomically
/// (temp file + rename) so a crash mid-save can never corrupt the library.
/// </summary>
public sealed class JsonFeedStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string DataPath { get; }

    public JsonFeedStore(string? dataPath = null)
    {
        DataPath = dataPath ?? DefaultDataPath();
    }

    /// <summary>Default location: ~/.local/share/feedreader/data.json (or the OS local-app-data dir).</summary>
    public static string DefaultDataPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("FEEDREADER_DATA");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(baseDir, "feedreader", "data.json");
    }

    public FeedDatabase Load()
    {
        if (!File.Exists(DataPath))
        {
            return new FeedDatabase();
        }

        var json = File.ReadAllText(DataPath);
        return JsonSerializer.Deserialize<FeedDatabase>(json, JsonOptions) ?? new FeedDatabase();
    }

    public void Save(FeedDatabase db)
    {
        var dir = Path.GetDirectoryName(DataPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Write to a temp file in the same directory, then rename: readers either
        // see the old file or the new one, never a half-written file.
        var tempPath = DataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(db, JsonOptions));
            File.Move(tempPath, DataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
