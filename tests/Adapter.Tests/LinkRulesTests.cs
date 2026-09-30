using Defuse.Domain;
using Defuse.Integrations;
using Defuse.Metadata.TMDB;
using Defuse.Sources.Direct;
using Defuse.Sources.Library;
using Defuse.Sources.Servers;
using Defuse.Sources.Stremio;

namespace Defuse.Adapter.Tests;

public class LinkRulesTests
{
    [Theory]
    [InlineData("https://cdn.example/video.mp4", LinkClass.DirectFile)]
    [InlineData("https://cdn.example/live.m3u8", LinkClass.Hls)]
    [InlineData("https://cdn.example/stream.mpd", LinkClass.Dash)]
    [InlineData("http://127.0.0.1:11470/video.mp4", LinkClass.LoopbackDirect)]
    [InlineData("https://example.com/manifest.json", LinkClass.AddonManifest)]
    [InlineData("stremio://detail/movie/tt1", LinkClass.StremioDeepLink)]
    [InlineData("magnet:?xt=urn:btih:abc", LinkClass.Magnet)]
    [InlineData("https://www.netflix.com/watch/1", LinkClass.ExternalPage)]
    [InlineData("https://cdn.example/no-extension", LinkClass.UnknownHttp)]
    public void Classifier_sorts_known_inputs(string input, LinkClass expected)
    {
        Assert.Equal(expected, LinkClassifier.Classify(input).Class);
    }

    [Fact]
    public void Folder_unc_and_playlist_messages_match_the_shell()
    {
        Assert.Equal("This folder can be scanned into the library.", LinkClassifier.Classify(@"C:\Videos\").UserMessage);
        Assert.Equal("Network folders can be added under Settings, Connections.", LinkClassifier.Classify(@"\\nas\media").UserMessage);
        Assert.Contains("folder scan", LinkClassifier.Classify(@"C:\Videos\list.m3u").UserMessage);
        Assert.Equal("This is an addon link. Add it under Settings, Connections.", LinkClassifier.Classify("https://addons.example/manifest.json").UserMessage);
    }

    [Fact]
    public void Head_is_not_required_for_a_direct_file()
    {
        var link = LinkClassifier.Classify("https://cdn.example/clip.mkv?token=abc");
        Assert.True(link.IsPlayableNow);
        Assert.True(link.MayExpire);
        Assert.DoesNotContain("abc", link.RedactedDisplay);
    }

    [Fact]
    public void Failure_copy_points_at_the_cause()
    {
        Assert.Contains("Stremio", PlaybackFailureCopy.Describe(true, true, false, false));
        Assert.Contains("this PC", PlaybackFailureCopy.Describe(true, false, false, false));
        Assert.Contains("Update this video's link", PlaybackFailureCopy.Describe(false, false, true, false));
        Assert.Contains("still in your library", PlaybackFailureCopy.Describe(false, false, false, false));
    }

    [Fact]
    public void Filename_parser_reads_season_markers()
    {
        var episode = FilenameParser.Parse(@"D:\Shows\Example Show S02E03.mkv");
        Assert.Equal("episode", episode.Type);
        Assert.Equal(2, episode.Season);
        Assert.Equal(3, episode.Episode);
        Assert.Equal("Example Show", episode.ShowTitle);

        var cross = FilenameParser.Parse(@"D:\Shows\Example Show 1x02.mkv");
        Assert.Equal(1, cross.Season);
        Assert.Equal(2, cross.Episode);

        var named = FilenameParser.Parse("Andor.S01E01.Kassa.UHD.BluRay.2160p.TrueHD.Atmos.7.1.DV.HEVC.HYBRID.REMUX-FraMeSToR.mkv");
        Assert.Equal("Andor", named.ShowTitle);
        Assert.Equal("Kassa", named.EpisodeTitle);
        Assert.Equal("Andor - Kassa", named.Title);
        Assert.Equal("Example Show", episode.Title);
        Assert.Equal("Andor - Kassa", FilenameParser.DisplayTitle("Andor S01E01 Kassa"));
        Assert.Equal("Transformers The Last Knight", FilenameParser.Parse("Transformers.The.Last.Knight.(2017).mkv").Title);
        Assert.Equal("Transformers The Last Knight", FilenameParser.DisplayTitle("Transformers The Last Knight ("));
    }

    [Fact]
    public void Stremio_resource_uri_strips_manifest_suffix()
    {
        var uri = StremioManifest.ResourceUri(new Uri("https://addon.example/manifest.json"), "stream", "movie", "tt123");
        Assert.Equal("https://addon.example/stream/movie/tt123.json", uri.AbsoluteUri);
        var parsed = StremioManifest.ParseStreams("""{"streams":[{"url":"https://cdn.example/a.mp4","title":"A"}]}""");
        Assert.Equal("https://cdn.example/a.mp4", parsed[0].Url);
    }

    [Fact]
    public async Task Tmdb_without_a_key_returns_nothing()
    {
        var client = new TmdbClient(new HttpClient());
        var matches = await client.SearchMovieAsync("", "Example", 2020, CancellationToken.None);
        Assert.Empty(matches);
        Assert.Contains("TMDB", TmdbClient.Attribution);
    }

    [Fact]
    public void Export_and_trakt_and_cloud_do_not_invent_access()
    {
        var json = LibraryExchange.ToJson([new ExportTitle("Demo", "video", 10, false, null)]);
        Assert.DoesNotContain("http", json);
        Assert.False(TraktQueue.CanSend(null));
        Assert.DoesNotContain("token", TraktQueue.ScrobbleBody("Demo", 10, 100, true));
        Assert.Equal("This cloud is not connected. Each provider needs its own sign-in.", CloudCatalog.NotConnected);
        Assert.Contains("Dropbox", CloudCatalog.Providers);
    }

    [Fact]
    public void Remote_helpers_describe_real_limits()
    {
        Assert.Contains("Mount the NFS", NfsMount.Describe(@"C:\missing-nfs-share"));
        var ftp = PlaybackUris.FtpFile("nas.example", 21, "/clip.mp4", "viewer");
        Assert.Equal("ftp", ftp.Scheme);
        var jellyfin = PlaybackUris.JellyfinVideo(new Uri("http://nas.example:8096"), "item", "secret-token");
        Assert.Contains("api_key=", jellyfin.AbsoluteUri);
        Assert.DoesNotContain("secret-token", UrlRedactor.Redact(jellyfin));
    }
}
