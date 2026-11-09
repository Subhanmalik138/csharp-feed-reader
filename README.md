# FeedReader

A CLI RSS/Atom feed aggregator in C# (.NET 8, zero third-party dependencies in the app itself). Subscribe to feeds, fetch new items on demand, and read them from the terminal — with unread tracking, starring, full-text search, per-feed stats, and OPML import/export so your subscription list can move to any other reader.

## Why this exists

Most feed readers are web apps or Electron bundles. FeedReader is the opposite: a single small binary, one JSON file of state, no server, no account. It is built to be read as much as used — the interesting parts are the feed parser (RSS 2.0 and Atom 1.0, namespace-agnostic), the conditional-GET fetcher, and the atomic JSON store.

## Features

- **Subscribe / unsubscribe** — `add`, `remove`, `feeds`, with optional categories for grouping
- **Fetch** — `refresh` pulls every feed (or one feed), using `ETag` / `Last-Modified` conditional GETs so unchanged feeds cost a single 304
- **Read** — `list`, `show`, `read` / `unread`, `star` / `unstar`
- **Search** — case-insensitive substring search across title, description and author, with `--feed`, `--unread`, `--since` and `--limit` filters
- **Stats** — totals plus per-feed item counts, unread counts, last fetch time and last error
- **Import / export** — OPML 2.0 both ways (folders become categories); items export as JSON or CSV
- **Safe storage** — the whole library is one JSON file, written atomically (temp file + rename) so a crash can never corrupt it; item identity is `(FeedId, Guid)`, so refreshes never duplicate entries

## Design decisions

1. **Namespace-agnostic parsing.** RSS and Atom feeds disagree on namespaces, prefixes and extensions (Dublin Core, `content:encoded`). The parser matches on local element names only, so all of those parse the same way.
2. **Plain-text descriptions.** HTML from feeds is stripped and entities decoded at parse time. Search and display then work on clean text, and nothing executable is ever stored.
3. **Conditional GETs, not polling timers.** Each feed remembers its `ETag` and `Last-Modified`, so a refresh of an unchanged feed is one cheap 304 instead of a full re-download and re-parse.
4. **No database, no ORM.** The data model is two lists (feeds, items) in one JSON file. For a personal reader this is simpler, inspectable, and trivially backup-able — and the atomic-write store keeps it safe.

## Quick start

```bash
dotnet run --project src/FeedReader -- add https://example.com/feed.xml --category tech
dotnet run --project src/FeedReader -- refresh
dotnet run --project src/FeedReader -- list --unread
dotnet run --project src/FeedReader -- search "release notes"
```

Or build once and use the binary:

```bash
dotnet build src/FeedReader -c Release
./src/FeedReader/bin/Release/net8.0/feedreader --help
```

## Usage

```
feedreader [--data <path>] <command> [options]

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
```

Feed and item ids are short (8 hex chars) and can be abbreviated — any unique prefix works, e.g. `feedreader read a3f9`.

Data lives in `~/.local/share/feedreader/data.json` by default. Override it per-run with `--data <path>` or permanently with the `FEEDREADER_DATA` environment variable.

### Examples

```bash
# Subscribe and fetch
feedreader add https://blog.example.com/rss.xml --title "Example Blog" --category tech
feedreader refresh

# Morning catch-up: unread items from the last day
feedreader list --unread --since 2026-11-08

# Find everything about a topic, then open the first hit
feedreader search "rust"
feedreader show a3f9c21e

# Move subscriptions to another reader
feedreader export-opml subscriptions.opml

# Monthly reading report
feedreader export --format csv --output november.csv
```

## Running the tests

```bash
dotnet test
```

27 tests cover the RSS/Atom parser (including date formats and HTML stripping), the fetcher (deduplication, 304 handling, HTTP and parse errors), the atomic JSON store, search/filter queries, and the OPML round-trip.

## Tech stack

- C# 12 / .NET 8, no application dependencies (only `System.Text.Json` and `System.Xml.Linq` from the framework)
- xUnit + Microsoft.NET.Test.Sdk for tests
- `HttpClient` for fetching, `XDocument` for parsing

## Project layout

```
FeedReader.sln
src/FeedReader/
  Program.cs        CLI dispatcher and commands
  Models.cs         Feed, FeedItem, FeedDatabase
  JsonFeedStore.cs  atomic JSON persistence
  FeedParser.cs     RSS 2.0 / Atom 1.0 parsing, HTML stripping
  FeedFetcher.cs    conditional-GET fetching, dedupe merge
  FeedSearch.cs     list/search query engine
  Opml.cs           OPML 2.0 import/export
  ItemExporter.cs   JSON/CSV item export
tests/FeedReader.Tests/
  FeedParserTests.cs, FeedFetcherTests.cs,
  StoreAndSearchTests.cs, OpmlTests.cs
```

## License

MIT — see [LICENSE](LICENSE).
