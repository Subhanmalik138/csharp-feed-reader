namespace FeedReader.Tests;

/// <summary>Shared sample feeds used by parser and fetcher tests.</summary>
public static class SampleFeeds
{
    public const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel>
            <title>Example Blog</title>
            <link>https://example.com</link>
            <description>A sample blog feed.</description>
            <item>
              <title>First Post</title>
              <link>https://example.com/first</link>
              <guid>guid-1</guid>
              <description><![CDATA[<p>Hello <b>world</b> &amp; friends.</p>]]></description>
              <dc:creator>Alice</dc:creator>
              <pubDate>Mon, 09 Nov 2026 10:00:00 GMT</pubDate>
            </item>
            <item>
              <title>Second Post</title>
              <link>https://example.com/second</link>
              <description>Plain text summary.</description>
              <pubDate>Tue, 10 Nov 2026 08:30:00 +0000</pubDate>
            </item>
          </channel>
        </rss>
        """;

    public const string Atom = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Atom Journal</title>
          <link rel="alternate" href="https://atom.example.com/"/>
          <link rel="self" href="https://atom.example.com/feed.xml"/>
          <entry>
            <title>Atom Entry One</title>
            <link rel="alternate" href="https://atom.example.com/one"/>
            <id>urn:atom:1</id>
            <summary>First atom entry.</summary>
            <author><name>Bob</name></author>
            <published>2026-11-09T12:00:00Z</published>
          </entry>
          <entry>
            <title>Atom Entry Two</title>
            <link href="https://atom.example.com/two"/>
            <id>urn:atom:2</id>
            <content type="html">&lt;p&gt;With &lt;em&gt;markup&lt;/em&gt;.&lt;/p&gt;</content>
            <updated>2026-11-08T09:15:00Z</updated>
          </entry>
        </feed>
        """;
}
