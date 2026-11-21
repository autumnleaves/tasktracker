using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TaskTracker.Api.Board;

namespace TaskTracker.Api.Tests;

/// <summary>The API with its own SQLite file in a temp folder, deleted on dispose.</summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public TestApp(string? folder = null)
    {
        Folder = folder ?? Path.Combine(Path.GetTempPath(), "tasktracker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }
    public string DbPath => Path.Combine(Folder, "tasks.db");

    // App configuration rather than UseSetting, so it wins over appsettings.json.
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Tasks"] = $"Data Source={DbPath}" }));

    public async Task<BoardView> Board() =>
        (await CreateClient().GetFromJsonAsync<BoardView>("/api/board", Json))!;

    public async Task<Card> Create(object request)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/cards", request, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Card>(Json))!;
    }

    public async Task<Card> Move(long id, string status, int? index = null)
    {
        var response = await CreateClient().PostAsJsonAsync($"/api/cards/{id}/move", new { status, index }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Card>(Json))!;
    }

    public async Task<Label> CreateLabel(string name, string color = "#3d6fa8")
    {
        var response = await CreateClient().PostAsJsonAsync("/api/labels", new { name, color }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Label>(Json))!;
    }

    public async Task<IReadOnlyList<long>> ColumnOrder(CardStatus status) =>
        [.. (await Board()).Cards.Where(c => c.Status == status).Select(c => c.Id)];

    public void DeleteFolder()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
    }
}
