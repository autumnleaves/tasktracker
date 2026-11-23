using System.Net;
using System.Net.Http.Json;
using System.Text;
using TaskTracker.Api.Board;

namespace TaskTracker.Api.Tests;

/// <summary>The parts of the API meant for scripts and agents: filters, PATCH, label names, comments.</summary>
public sealed class AgentApiTests : IDisposable
{
    private readonly TestApp _app = new();
    private readonly HttpClient _client;

    public AgentApiTests() => _client = _app.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        _app.DeleteFolder();
    }

    private async Task<HttpResponseMessage> Patch(long id, string json) =>
        await _client.PatchAsync($"/api/cards/{id}", new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<Card> PatchOk(long id, string json)
    {
        var response = await Patch(id, json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Card>(TestApp.Json))!;
    }

    private async Task<List<Card>> List(string query) =>
        (await _client.GetFromJsonAsync<List<Card>>($"/api/cards{query}", TestApp.Json))!;

    [Fact]
    public async Task Cards_can_be_made_with_label_names_which_are_created_when_new()
    {
        var existing = await _app.CreateLabel("Homelab", "#2f8f6b");
        var card = await _app.Create(new { title = "Tunnel", labels = new[] { "homelab", "cloudflare" } });

        Assert.Equal(2, card.LabelIds.Count);
        Assert.Contains(existing.Id, card.LabelIds);
        Assert.Contains("Homelab", card.Labels);
        Assert.Contains("cloudflare", card.Labels);
        var labels = await _client.GetFromJsonAsync<List<Label>>("/api/labels", TestApp.Json);
        Assert.Equal(2, labels!.Count);
        Assert.Matches("^#[0-9a-f]{6}$", labels.Single(l => l.Name == "cloudflare").Color);
    }

    [Fact]
    public async Task Listing_filters_by_status_label_priority_and_text()
    {
        var a = await _app.Create(new { title = "Write the skill", status = "in_progress", priority = "high", labels = new[] { "claude" } });
        var b = await _app.Create(new { title = "Buy milk", status = "todo" });
        var c = await _app.Create(new { title = "Old thing", status = "done", labels = new[] { "claude" } });

        Assert.Equal(new[] { a.Id }, (await List("?status=in_progress")).Select(x => x.Id));
        Assert.Equal(new[] { b.Id, a.Id }, (await List("?status=todo&status=in_progress")).Select(x => x.Id));
        Assert.Equal(new[] { a.Id, c.Id }, (await List("?label=Claude")).Select(x => x.Id));
        Assert.Equal(new[] { a.Id }, (await List("?label=claude&includeDone=false")).Select(x => x.Id));
        Assert.Equal(new[] { a.Id }, (await List("?priority=high")).Select(x => x.Id));
        Assert.Equal(new[] { b.Id }, (await List("?q=MILK")).Select(x => x.Id));
        Assert.Equal(new[] { c.Id }, (await List($"?q=%23{c.Id}")).Select(x => x.Id));
        Assert.Equal(3, (await List("")).Count);
    }

    [Fact]
    public async Task Bad_filters_are_rejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/cards?status=doing")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/cards?priority=meh")).StatusCode);
    }

    [Fact]
    public async Task Patch_changes_only_what_is_sent()
    {
        var card = await _app.Create(new
        {
            title = "Original", description = "keep me", priority = "low", dueDate = "2026-12-01", labels = new[] { "x" },
        });

        var patched = await PatchOk(card.Id, """{"title":"Renamed"}""");

        Assert.Equal("Renamed", patched.Title);
        Assert.Equal("keep me", patched.Description);
        Assert.Equal(Priority.Low, patched.Priority);
        Assert.Equal("2026-12-01", patched.DueDate);
        Assert.Equal(card.LabelIds, patched.LabelIds);
    }

    [Fact]
    public async Task Patch_null_clears_the_due_date_and_labels_replace()
    {
        var card = await _app.Create(new { title = "Due", dueDate = "2026-12-01", labels = new[] { "a", "b" } });

        var patched = await PatchOk(card.Id, """{"dueDate":null,"labels":["c"],"priority":"urgent"}""");

        Assert.Null(patched.DueDate);
        Assert.Equal(new[] { "c" }, patched.Labels);
        Assert.Equal(Priority.Urgent, patched.Priority);
    }

    [Fact]
    public async Task Patch_status_moves_the_card_to_the_bottom_or_to_an_index()
    {
        var first = await _app.Create(new { title = "First", status = "in_progress" });
        var card = await _app.Create(new { title = "Moving", status = "todo" });

        var moved = await PatchOk(card.Id, """{"status":"in_progress"}""");
        Assert.Equal(CardStatus.InProgress, moved.Status);
        Assert.Equal(new long[] { first.Id, card.Id }, await _app.ColumnOrder(CardStatus.InProgress));

        await PatchOk(card.Id, """{"index":0}""");
        Assert.Equal(new long[] { card.Id, first.Id }, await _app.ColumnOrder(CardStatus.InProgress));

        var done = await PatchOk(card.Id, """{"status":"done"}""");
        Assert.NotNull(done.CompletedAt);
    }

    [Theory]
    [InlineData("""{"colour":"red"}""", "colour")]
    [InlineData("""{"title":""}""", "title")]
    [InlineData("""{"status":"doing"}""", "status")]
    [InlineData("""{"priority":null}""", "priority")]
    [InlineData("""{"dueDate":"tomorrow"}""", "dueDate")]
    [InlineData("""{"labelIds":[999]}""", "labelIds")]
    [InlineData("""{"index":-1}""", "index")]
    public async Task Patch_rejects_bad_fields(string json, string field)
    {
        var card = await _app.Create(new { title = "Target" });

        var response = await Patch(card.Id, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>(TestApp.Json);
        Assert.Contains(field, problem!.Errors.Keys);
        Assert.Equal("Target", (await _client.GetFromJsonAsync<Card>($"/api/cards/{card.Id}", TestApp.Json))!.Title);
    }

    [Fact]
    public async Task Patch_needs_a_json_object_and_an_existing_card()
    {
        var card = await _app.Create(new { title = "Target" });
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch(card.Id, "[1,2]")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch(card.Id, "not json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Patch(12345, """{"title":"x"}""")).StatusCode);
    }

    [Fact]
    public async Task Comments_are_added_listed_counted_and_deleted()
    {
        var card = await _app.Create(new { title = "Discussed" });

        var first = await _client.PostAsJsonAsync($"/api/cards/{card.Id}/comments", new { body = "Starting on this", author = "Claude" }, TestApp.Json);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await _client.PostAsJsonAsync($"/api/cards/{card.Id}/comments", new { body = "Thanks" }, TestApp.Json);

        var comments = await _client.GetFromJsonAsync<List<Comment>>($"/api/cards/{card.Id}/comments", TestApp.Json);
        Assert.Equal(new[] { "Claude", "Tom" }, comments!.Select(c => c.Author));
        Assert.Equal("Starting on this", comments[0].Body);

        var reloaded = await _client.GetFromJsonAsync<Card>($"/api/cards/{card.Id}", TestApp.Json);
        Assert.Equal(2, reloaded!.CommentCount);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/comments/{comments[0].Id}")).StatusCode);
        Assert.Single((await _client.GetFromJsonAsync<List<Comment>>($"/api/cards/{card.Id}/comments", TestApp.Json))!);
    }

    [Fact]
    public async Task Comments_need_a_body_and_a_card()
    {
        var card = await _app.Create(new { title = "Quiet" });
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync($"/api/cards/{card.Id}/comments", new { body = "  " }, TestApp.Json)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync("/api/cards/12345/comments", new { body = "hi" }, TestApp.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cards/12345/comments")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_card_deletes_its_comments()
    {
        var card = await _app.Create(new { title = "Doomed" });
        var response = await _client.PostAsJsonAsync($"/api/cards/{card.Id}/comments", new { body = "bye" }, TestApp.Json);
        var comment = (await response.Content.ReadFromJsonAsync<Comment>(TestApp.Json))!;

        await _client.DeleteAsync($"/api/cards/{card.Id}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/comments/{comment.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_api_describes_itself()
    {
        var json = await _client.GetStringAsync("/api/openapi.json");
        Assert.Contains("\"/api/cards/{id}\"", json);
        Assert.Contains("patch", json);
    }

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
