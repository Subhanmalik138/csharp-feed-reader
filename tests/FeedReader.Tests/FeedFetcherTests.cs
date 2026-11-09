using Xunit;
using System.Net;
using FeedReader;

namespace FeedReader.Tests;

public sealed class FeedFetcherTests
{
    /// <summary>Returns canned responses in order, capturing the requests sent.</summary>
    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static HttpResponseMessage Ok(string body, string? eTag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
        if (eTag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(eTag);
        }
        return response;
    }

    private static (Feed Feed, FeedDatabase Db) NewFeed() =>
        (new Feed { Url = "https://example.com/feed.xml", Title = "Example" }, new FeedDatabase());

    [Fact]
    public async Task Fetch_AddsNewItems_AndUpdatesFeedMetadata()
    {
        var handler = new QueueHandler();
        handler.Enqueue(Ok(SampleFeeds.Rss, eTag: "\"v1\""));
        var (feed, db) = NewFeed();

        var result = await new FeedFetcher(new HttpClient(handler)).FetchAsync(feed, db);

        Assert.True(result.Success);
        Assert.Equal(2, result.NewItems);
        Assert.Equal(2, db.Items.Count);
        Assert.Equal("Example Blog", feed.Title);
        Assert.Equal("https://example.com", feed.SiteUrl);
        Assert.Equal("\"v1\"", feed.ETag);
        Assert.Null(feed.LastError);
        Assert.Equal(1, feed.FetchCount);
    }

    [Fact]
    public async Task SecondFetch_Deduplicates_ByGuid()
    {
        var handler = new QueueHandler();
        handler.Enqueue(Ok(SampleFeeds.Rss));
        handler.Enqueue(Ok(SampleFeeds.Rss));
        var (feed, db) = NewFeed();
        var fetcher = new FeedFetcher(new HttpClient(handler));

        await fetcher.FetchAsync(feed, db);
        var second = await fetcher.FetchAsync(feed, db);

        Assert.True(second.Success);
        Assert.Equal(0, second.NewItems);
        Assert.Equal(2, db.Items.Count);
    }

    [Fact]
    public async Task NotModified_304_AddsNothing_AndSendsConditionalHeaders()
    {
        var handler = new QueueHandler();
        handler.Enqueue(Ok(SampleFeeds.Rss, eTag: "\"v1\""));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NotModified));
        var (feed, db) = NewFeed();
        var fetcher = new FeedFetcher(new HttpClient(handler));

        await fetcher.FetchAsync(feed, db);
        var second = await fetcher.FetchAsync(feed, db);

        Assert.True(second.Success);
        Assert.True(second.NotModified);
        Assert.Equal(2, db.Items.Count);
        // Second request carried the ETag from the first response.
        Assert.NotEmpty(handler.Requests[1].Headers.IfNoneMatch);
    }

    [Fact]
    public async Task HttpError_SetsLastError_AndReportsFailure()
    {
        var handler = new QueueHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" });
        var (feed, db) = NewFeed();

        var result = await new FeedFetcher(new HttpClient(handler)).FetchAsync(feed, db);

        Assert.False(result.Success);
        Assert.Contains("404", result.Error);
        Assert.Contains("404", feed.LastError);
        Assert.Empty(db.Items);
    }

    [Fact]
    public async Task InvalidFeedXml_SetsLastError()
    {
        var handler = new QueueHandler();
        handler.Enqueue(Ok("<html>not a feed</html>"));
        var (feed, db) = NewFeed();

        var result = await new FeedFetcher(new HttpClient(handler)).FetchAsync(feed, db);

        Assert.False(result.Success);
        Assert.NotNull(feed.LastError);
        Assert.Empty(db.Items);
    }
}
