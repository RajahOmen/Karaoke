using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Karaoke.Services;

public partial class SongRequestIssueService
{
    private static readonly string GithubRepoIssuesApiLink = "https://api.github.com/repos/RajahOmen/karaoke-lyrics/issues";
    private static readonly string GithubRepoNewSongTitlePrefix = "[New Song Lyrics]";
    private static readonly Regex SongIdRegex = RegexSongIds();
    
    private static readonly string GithubRepoIssuesLink = "https://www.github.com/RajahOmen/karaoke-lyrics/issues";
    private static readonly string GithubRepoNewIssuesLink = $"https://www.github.com/RajahOmen/karaoke-lyrics/issues/new";
    
    private static readonly string IssueFormTemplate = "song-lyrics-request-form.yml";
    
    private readonly ExcelSheet<Map> mapSheet;
    private readonly IPluginLog pluginLog;
    private readonly BGMService bgmService;
    private readonly IClientState clientState;
    private readonly HttpClient httpClient;
    private Dictionary<uint, HashSet<int>> currentSongIdIssues = [];


    public SongRequestIssueService(IPluginLog pluginLog, BGMService bgmService, IDataManager dataManager, IClientState clientState, HttpClient httpClient)
    {
        this.pluginLog = pluginLog;
        this.bgmService = bgmService;
        this.clientState = clientState;
        this.httpClient = httpClient;
        mapSheet = dataManager.GetExcelSheet<Map>(ClientLanguage.English);
        _ = getCurrentIssueSongIds();
    }
    
    private readonly record struct PinnedComment
    {
        [JsonPropertyName("body")]
        public string? Body {
            get;
            init => field = value?.Trim();
        }

        [JsonPropertyName("author_association")]
        public string? AuthorAssociation { get; init; }

        public IEnumerable<uint> SongIds
        {
            get
            {
                var numIds = 0;
                foreach (Match match in SongIdRegex.Matches(Body ?? ""))
                {
                    // only 15 ids per issue allowed
                    if (numIds >= 15)
                        yield break;
                    if (uint.TryParse(match.Value, out var songId))
                    {
                        numIds++;
                        yield return songId;
                    }
                }
            }
        }
    }

    private readonly record struct GithubIssue
    {
        [JsonPropertyName("number")]
        public int Number { get; init; }

        [JsonPropertyName("pinned_comment")]
        public PinnedComment? Comment
        {
            get;
            init
            {
                // only look at pinned comment made by repo owner
                if (value?.AuthorAssociation == "OWNER")
                    field = value;
                else
                    field = null;
            }
        }

        public IEnumerable<uint> SongIds => Comment?.SongIds ?? [];
    }

    private async Task getCurrentIssueSongIds()
    {
        try
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["labels"] = "new song";
            query["per_page"] = "100";
            var issuesApiUri = new UriBuilder(GithubRepoIssuesApiLink)
            {
                Query = query.ToString(),
            };

            List<GithubIssue> issues;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, issuesApiUri.Uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

                using var response = await httpClient.SendAsync(request);
                pluginLog.Verbose($"Sent karaoke-lyrics issues request: {response.RequestMessage}");

                if (!response.IsSuccessStatusCode)
                {
                    pluginLog.Warning($"Unsuccessful request [{(int)response.StatusCode}] {response.ReasonPhrase}");
                    return;
                }
                
                try
                {
                    if (await response.Content.ReadFromJsonAsync<List<GithubIssue>>() is not { } responseIssues)
                    {
                        pluginLog.Warning($"karaoke-lyrics issue response parse is null: {response.Content}");
                        return;
                    }
                    pluginLog.Verbose($"Found {responseIssues.Count} open \"new song\" issues");
                    issues = responseIssues;
                }
                catch (JsonException ex)
                {
                    var contentString = await response.Content.ReadAsStringAsync();
                    contentString = contentString.Length > 500 ? contentString[..500] : contentString;
                    pluginLog.Warning(ex, $"Unable to parse issues response:\n{contentString}");
                    return;
                }
            }
            catch (Exception ex)
            {
                pluginLog.Warning(ex, "Error requesting karaoke-lyrics issues");
                return;
            }

            Dictionary<uint, HashSet<int>> newSongIdIssues = [];
            foreach (var issue in issues)
            {
                foreach (var songId in issue.SongIds)
                {
                    pluginLog.Verbose($"[Issue #{issue.Number}] Song id: \"{songId}\"");
                    if (!newSongIdIssues.TryGetValue(songId, out var issueNums))
                    {
                        issueNums = [];
                        newSongIdIssues[songId] = issueNums;
                    }

                    issueNums.Add(issue.Number);
                }
            }
            
            currentSongIdIssues = newSongIdIssues;
            pluginLog.Debug($"Got {currentSongIdIssues.Count} distinct songs mentioned in new song issues");
        }
        catch (Exception ex)
        {
            pluginLog.Warning(ex, "Error getting current issue song ids");
        }
    }
    
    private string? getCurrentLocation()
    {
        if (!clientState.IsLoggedIn)
            return null;

        if (clientState.MapId == 0)
            return null;

        if (!mapSheet.TryGetRow(clientState.MapId, out var row))
            return null;

        List<RowRef<PlaceName>> placeRefs = [row.PlaceNameRegion, row.PlaceName, row.PlaceNameSub];

        var locNameStrings = placeRefs
            .Where(r => r is { IsValid: true, RowId: > 0 })
            .Select(r => r.ValueNullable?.Name.ToString())
            .Where(n => !n.IsNullOrEmpty() && n != "???")
            .ToHashSet();
        
        return locNameStrings.Count == 0 ? $"Map id {row.Id}" : string.Join(" / ", locNameStrings);
    }

    public (Uri? Uri, IReadOnlySet<int> Issues) CurrentSongRequestUri()
    {
        if (bgmService is not { CurrentSong: { } song })
            return (null, new HashSet<int>());
        
        var issueSongName = song.Name ?? song.FileName;
        var songTitle = issueSongName ?? song.Id.ToString();

        var openSongIssues = currentSongIdIssues.GetValueOrDefault(song.Id) ?? [];
        if (openSongIssues.Count > 0)
        {
            return (new Uri($"{GithubRepoIssuesLink}/{openSongIssues.Min()}"), openSongIssues);
        }

        var location = getCurrentLocation();

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["template"] = IssueFormTemplate;
        if (!issueSongName.IsNullOrWhitespace())
            query["song_name"] = issueSongName;
        query["title"] = $"{GithubRepoNewSongTitlePrefix} {songTitle}";
        query["song_ids"] = $"{(location is not null ? $"Location(s): {location}\n" : "")}Id(s): {song.Id}, ";

        return (
            new UriBuilder(GithubRepoNewIssuesLink)
            {
               Query = query.ToString()
            }.Uri,
            openSongIssues
        );
    }

    [GeneratedRegex(@"(?<!\d)\d{3,10}(?!\d)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexSongIds();
}
