using System.Net;
using System.Net.Http.Json;
using TaskTracker.Api.Board;

namespace TaskTracker.Api.Tests;

public sealed class BoardApiTests : IDisposable
{
    private readonly TestApp _app = new();
    private readonly HttpClient _client;

    public BoardApiTests() => _client = _app.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        _app.DeleteFolder();
    }

    [Fact]
    public async Task Health_answers_ok()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_board_has_the_four_columns()
    {
        var board = await _app.Board();
        Assert.Equal(
            new[] { CardStatus.Backlog, CardStatus.Todo, CardStatus.InProgress, CardStatus.Done },
            board.Columns.Select(c => c.Id));
        Assert.Empty(board.Cards);
    }

    [Fact]
    public async Task Statuses_are_snake_case_in_json()
    {
        await _app.Create(new { title = "Write tests", status = "in_progress" });
        var json = await _client.GetStringAsync("/api/board");
        Assert.Contains("\"status\":\"in_progress\"", json);
        Assert.Contains("\"priority\":\"medium\"", json);
    }

    [Fact]
    public async Task A_new_card_goes_to_the_bottom_of_its_column_with_defaults()
    {
        var first = await _app.Create(new { title = "First" });
        var second = await _app.Create(new { title = "  Second  " });

        Assert.Equal("Second", second.Title);
        Assert.Equal(CardStatus.Backlog, second.Status);
        Assert.Equal(Priority.Medium, second.Priority);
        Assert.True(second.Position > first.Position);
        Assert.Equal(new long[] { first.Id, second.Id }, await _app.ColumnOrder(CardStatus.Backlog));
    }

    [Theory]
    [InlineData("""{"title":""}""", "title")]
    [InlineData("""{"title":"   "}""", "title")]
    [InlineData("""{"title":"x","dueDate":"30/11/2026"}""", "dueDate")]
    [InlineData("""{"title":"x","labelIds":[999]}""", "labelIds")]
    public async Task Invalid_cards_are_rejected(string body, string field)
    {
        var response = await _client.PostAsync(
            "/api/cards", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>(TestApp.Json);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task An_unknown_status_is_rejected()
    {
        var response = await _client.PostAsync(
            "/api/cards",
            new StringContent("""{"title":"x","status":"doing"}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Moving_reorders_within_a_column()
    {
        var a = await _app.Create(new { title = "A", status = "todo" });
        var b = await _app.Create(new { title = "B", status = "todo" });
        var c = await _app.Create(new { title = "C", status = "todo" });

        await _app.Move(c.Id, "todo", 0);
        Assert.Equal(new long[] { c.Id, a.Id, b.Id }, await _app.ColumnOrder(CardStatus.Todo));

        await _app.Move(c.Id, "todo", 99);
        Assert.Equal(new long[] { a.Id, b.Id, c.Id }, await _app.ColumnOrder(CardStatus.Todo));
    }

    [Fact]
    public async Task Moving_to_another_column_inserts_at_the_index()
    {
        var a = await _app.Create(new { title = "A", status = "in_progress" });
        var b = await _app.Create(new { title = "B", status = "in_progress" });
        var moving = await _app.Create(new { title = "Moving", status = "todo" });

        var moved = await _app.Move(moving.Id, "in_progress", 1);

        Assert.Equal(CardStatus.InProgress, moved.Status);
        Assert.Equal(new long[] { a.Id, moving.Id, b.Id }, await _app.ColumnOrder(CardStatus.InProgress));
        Assert.Empty(await _app.ColumnOrder(CardStatus.Todo));
    }

    [Fact]
    public async Task Done_sets_and_clears_the_completion_time()
    {
        var card = await _app.Create(new { title = "Ship it", status = "in_progress" });
        Assert.Null(card.CompletedAt);

        var done = await _app.Move(card.Id, "done");
        Assert.NotNull(done.CompletedAt);

        var reopened = await _app.Move(card.Id, "todo");
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task Updating_replaces_the_editable_fields()
    {
        var urgent = await _app.CreateLabel("Urgent", "#c8323c");
        var homelab = await _app.CreateLabel("homelab");
        var card = await _app.Create(new { title = "Old", labelIds = new[] { urgent.Id } });

        var response = await _client.PutAsJsonAsync($"/api/cards/{card.Id}", new
        {
            title = "New",
            description = "- [ ] step one",
            priority = "high",
            dueDate = "2026-11-30",
            labelIds = new[] { homelab.Id },
        }, TestApp.Json);
        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<Card>(TestApp.Json))!;

        Assert.Equal("New", updated.Title);
        Assert.Equal("- [ ] step one", updated.Description);
        Assert.Equal(Priority.High, updated.Priority);
        Assert.Equal("2026-11-30", updated.DueDate);
        Assert.Equal(new long[] { homelab.Id }, updated.LabelIds);
        Assert.Equal(card.Status, updated.Status);

        // An empty due date clears it.
        response = await _client.PutAsJsonAsync(
            $"/api/cards/{card.Id}", new { title = "New", dueDate = "", priority = "high" }, TestApp.Json);
        updated = (await response.Content.ReadFromJsonAsync<Card>(TestApp.Json))!;
        Assert.Null(updated.DueDate);
        Assert.Empty(updated.LabelIds);
    }

    [Fact]
    public async Task Deleting_a_card_removes_it()
    {
        var card = await _app.Create(new { title = "Gone soon" });

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/cards/{card.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/cards/{card.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/cards/{card.Id}")).StatusCode);
    }

    [Fact]
    public async Task Card_numbers_are_never_reused()
    {
        var first = await _app.Create(new { title = "One" });
        await _client.DeleteAsync($"/api/cards/{first.Id}");
        var second = await _app.Create(new { title = "Two" });
        Assert.True(second.Id > first.Id);
    }

    [Fact]
    public async Task Missing_cards_are_404s()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cards/12345")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync("/api/cards/12345/move", new { status = "done" }, TestApp.Json)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.PutAsJsonAsync("/api/cards/12345", new { title = "x" }, TestApp.Json)).StatusCode);
    }

    [Fact]
    public async Task Label_names_are_unique_ignoring_case()
    {
        await _app.CreateLabel("Homelab");
        var response = await _client.PostAsJsonAsync("/api/labels", new { name = "homelab", color = "#123456" }, TestApp.Json);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("", "#123456", "name")]
    [InlineData("ok", "blue", "color")]
    [InlineData("ok", "#12345", "color")]
    public async Task Invalid_labels_are_rejected(string name, string color, string field)
    {
        var response = await _client.PostAsJsonAsync("/api/labels", new { name, color }, TestApp.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>(TestApp.Json);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task Deleting_a_label_takes_it_off_cards()
    {
        var label = await _app.CreateLabel("Later");
        var card = await _app.Create(new { title = "Tagged", labelIds = new[] { label.Id } });

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/labels/{label.Id}")).StatusCode);

        var reloaded = await _client.GetFromJsonAsync<Card>($"/api/cards/{card.Id}", TestApp.Json);
        Assert.Empty(reloaded!.LabelIds);
    }

    [Fact]
    public async Task Renaming_a_label()
    {
        var label = await _app.CreateLabel("Wrok");
        var response = await _client.PutAsJsonAsync($"/api/labels/{label.Id}", new { name = "Work", color = "#ABCDEF" }, TestApp.Json);
        var renamed = (await response.Content.ReadFromJsonAsync<Label>(TestApp.Json))!;
        Assert.Equal("Work", renamed.Name);
        Assert.Equal("#abcdef", renamed.Color);
    }

    [Fact]
    public async Task Updating_sets_and_clears_the_project()
    {
        var project = await _app.CreateProject("Homelab");
        var card = await _app.Create(new { title = "Tunnel" });
        Assert.Null(card.ProjectId);

        var response = await _client.PutAsJsonAsync($"/api/cards/{card.Id}", new { title = "Tunnel", projectId = project.Id }, TestApp.Json);
        var updated = (await response.Content.ReadFromJsonAsync<Card>(TestApp.Json))!;
        Assert.Equal(project.Id, updated.ProjectId);
        Assert.Equal("Homelab", updated.Project);

        response = await _client.PutAsJsonAsync($"/api/cards/{card.Id}", new { title = "Tunnel" }, TestApp.Json);
        Assert.Null((await response.Content.ReadFromJsonAsync<Card>(TestApp.Json))!.ProjectId);

        response = await _client.PutAsJsonAsync($"/api/cards/{card.Id}", new { title = "Tunnel", projectId = 999 }, TestApp.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Project_names_are_unique_ignoring_case_and_can_be_renamed()
    {
        var project = await _app.CreateProject("Homlab");
        var response = await _client.PostAsJsonAsync("/api/projects", new { name = "homlab", color = "#123456" }, TestApp.Json);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        response = await _client.PostAsJsonAsync("/api/projects", new { name = "", color = "#123456" }, TestApp.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var card = await _app.Create(new { title = "Tunnel", projectId = project.Id });
        response = await _client.PutAsJsonAsync($"/api/projects/{project.Id}", new { name = "Homelab", color = "#ABCDEF" }, TestApp.Json);
        var renamed = (await response.Content.ReadFromJsonAsync<Project>(TestApp.Json))!;
        Assert.Equal("Homelab", renamed.Name);
        Assert.Equal("#abcdef", renamed.Color);
        Assert.Equal("Homelab", (await _client.GetFromJsonAsync<Card>($"/api/cards/{card.Id}", TestApp.Json))!.Project);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.PutAsJsonAsync("/api/projects/999", new { name = "x", color = "#123456" }, TestApp.Json)).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_project_keeps_its_cards_without_one()
    {
        var project = await _app.CreateProject("Abandoned");
        var card = await _app.Create(new { title = "Orphan", projectId = project.Id });

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/projects/{project.Id}")).StatusCode);

        var reloaded = await _client.GetFromJsonAsync<Card>($"/api/cards/{card.Id}", TestApp.Json);
        Assert.Null(reloaded!.ProjectId);
        Assert.Null(reloaded.Project);
        Assert.Empty((await _app.Board()).Projects);
    }

    [Fact]
    public async Task Unknown_api_routes_are_404s()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/nope/deeper", null)).StatusCode);
    }

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
