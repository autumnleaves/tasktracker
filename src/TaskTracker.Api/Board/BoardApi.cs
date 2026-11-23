using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace TaskTracker.Api.Board;

/// <summary>The HTTP API under <c>/api</c>. Validation lives here; storage in <see cref="BoardStore"/>.</summary>
public static partial class BoardApi
{
    public const int MaxTitle = 200;
    public const int MaxDescription = 20_000;
    public const int MaxLabelName = 40;
    public const int MaxProjectName = 60;

    private const string StatusHint = "Use backlog, todo, in_progress, in_review or done.";

    public static RouteGroupBuilder MapBoardApi(this RouteGroupBuilder api)
    {
        api.MapGet("/health", (BoardStore store) =>
        {
            store.Ping();
            return Results.Ok(new { status = "ok" });
        });

        api.MapGet("/board", (BoardStore store) => store.GetBoard());

        api.MapGet("/cards", ListCards)
            .WithSummary("Cards, filtered")
            .WithDescription(
                "Cards in board order (column, then position). All filters are optional and combine: status " +
                "(repeatable), label (a name; repeatable, all must match), project (a name; repeatable, any can " +
                "match), priority (repeatable), q (text in the title or description, or #<number>), and " +
                "includeDone=false to leave out Done.");
        api.MapPost("/cards", CreateCard)
            .WithSummary("Make a card")
            .WithDescription(
                "At the bottom of its column. title is required. labels takes names (made if new); labelIds takes " +
                "ids. project takes a project's name (made if new), projectId its id; send one or neither.");
        api.MapGet("/cards/{id:long}", (long id, BoardStore store) =>
            store.GetCard(id) is { } card ? Results.Ok(card) : Results.NotFound());
        api.MapPut("/cards/{id:long}", UpdateCard)
            .WithSummary("Replace a card's editable fields")
            .WithDescription(
                "title, description, priority, dueDate, labelIds and projectId. Left out, each goes back to its " +
                "default (no labels, no project). Its column and order change through a move or a PATCH.");
        api.MapPatch("/cards/{id:long}", PatchCard)
            .WithSummary("Change some of a card's fields")
            .WithDescription(
                "Only the fields sent change: title, description, priority, dueDate (null or \"\" clears it), " +
                "labels (names, made if new; replaces the card's labels), labelIds (ids; replaces), project (a " +
                "name, made if new) or projectId (null clears either), status (moves the card to the bottom of " +
                "that column, or to index if sent) and index.");
        api.MapPost("/cards/{id:long}/move", MoveCard);
        api.MapDelete("/cards/{id:long}", (long id, BoardStore store) =>
            store.DeleteCard(id) ? Results.NoContent() : Results.NotFound());

        api.MapGet("/cards/{id:long}/comments", (long id, BoardStore store) =>
                store.GetComments(id) is { } comments ? Results.Ok(comments) : Results.NotFound())
            .WithSummary("A card's comments, oldest first");
        api.MapPost("/cards/{id:long}/comments", AddComment)
            .WithSummary("Comment on a card")
            .WithDescription("body is required; author is who's writing, e.g. \"Tom\" or \"Claude\" (default \"Tom\").");
        api.MapDelete("/comments/{id:long}", (long id, BoardStore store) =>
            store.DeleteComment(id) ? Results.NoContent() : Results.NotFound());

        api.MapGet("/projects", (BoardStore store) => store.GetProjects())
            .WithSummary("Projects, by name")
            .WithDescription("Each card belongs to at most one project: an overarching piece of work, like a label but one per card.");
        api.MapPost("/projects", CreateProject)
            .WithSummary("Make a project")
            .WithDescription("name (unique, ignoring case) and color (#rrggbb).");
        api.MapPut("/projects/{id:long}", UpdateProject)
            .WithSummary("Rename or recolour a project");
        api.MapDelete("/projects/{id:long}", (long id, BoardStore store) =>
                store.DeleteProject(id) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Delete a project")
            .WithDescription("Its cards stay, without a project.");

        api.MapGet("/labels", (BoardStore store) => store.GetLabels());
        api.MapPost("/labels", CreateLabel);
        api.MapPut("/labels/{id:long}", UpdateLabel);
        api.MapDelete("/labels/{id:long}", (long id, BoardStore store) =>
            store.DeleteLabel(id) ? Results.NoContent() : Results.NotFound());

        // Anything else under /api is a 404, not the web app's index.html.
        api.MapFallback(() => Results.NotFound());
        return api;
    }

    // Statuses and priorities arrive as their JSON names (in_progress), which enum binding
    // wouldn't parse, hence strings.
    private static IResult ListCards(
        BoardStore store,
        [FromQuery(Name = "status")] string[]? statuses,
        [FromQuery] string[]? label,
        [FromQuery(Name = "project")] string[]? projects,
        [FromQuery(Name = "priority")] string[]? priorities,
        [FromQuery] string? q,
        [FromQuery] bool? includeDone)
    {
        var errors = new Errors();
        var status = (statuses ?? []).Select(code => Codes.TryParseStatus(code, out var st) ? st : (CardStatus?)null).ToArray();
        if (status.Any(st => st is null)) errors.Add("status", StatusHint);
        var priority = (priorities ?? []).Select(code => Codes.TryParsePriority(code, out var p) ? p : (Priority?)null).ToArray();
        if (priority.Any(p => p is null)) errors.Add("priority", "Use low, medium, high or urgent.");
        if (errors.Any) return errors.Result();

        var text = q?.Trim() ?? "";
        var number = text.StartsWith('#') ? text[1..] : text;
        var cards = store.GetBoard().Cards.Where(c =>
            (status.Length == 0 || status.Contains(c.Status)) &&
            (includeDone != false || c.Status != CardStatus.Done) &&
            (label is not { Length: > 0 } || label.All(l => c.Labels.Contains(l, StringComparer.OrdinalIgnoreCase))) &&
            (projects is not { Length: > 0 } || (c.Project is { } p && projects.Contains(p, StringComparer.OrdinalIgnoreCase))) &&
            (priority.Length == 0 || priority.Contains(c.Priority)) &&
            (text.Length == 0 ||
             c.Title.Contains(text, StringComparison.OrdinalIgnoreCase) ||
             c.Description.Contains(text, StringComparison.OrdinalIgnoreCase) ||
             c.Id.ToString(CultureInfo.InvariantCulture) == number));
        return Results.Ok(cards.ToList());
    }

    private static IResult CreateCard(CreateCardRequest request, BoardStore store)
    {
        var errors = new Errors();
        var title = errors.Title(request.Title);
        var description = errors.Description(request.Description);
        var dueDate = errors.DueDate(request.DueDate);
        var labelIds = errors.LabelIds(request.LabelIds, store);
        var labelNames = errors.LabelNames(request.Labels);
        var projectId = errors.ProjectId(request.ProjectId, store);
        var projectName = errors.ProjectName(request.Project);
        if (projectId is not null && projectName is not null) errors.Add("project", "Send project or projectId, not both.");
        if (errors.Any) return errors.Result();
        if (labelNames.Length > 0) labelIds = [.. labelIds.Union(store.LabelIdsByName(labelNames))];
        if (projectName is not null) projectId = store.ProjectIdByName(projectName);

        var card = store.CreateCard(
            title, description, request.Status ?? CardStatus.Backlog, request.Priority ?? Priority.Medium,
            dueDate, labelIds, projectId);
        return Results.Created($"/api/cards/{card.Id}", card);
    }

    private static IResult UpdateCard(long id, UpdateCardRequest request, BoardStore store)
    {
        var errors = new Errors();
        var title = errors.Title(request.Title);
        var description = errors.Description(request.Description);
        var dueDate = errors.DueDate(request.DueDate);
        var labelIds = errors.LabelIds(request.LabelIds, store);
        var projectId = errors.ProjectId(request.ProjectId, store);
        if (errors.Any) return errors.Result();

        return store.UpdateCard(id, title, description, request.Priority ?? Priority.Medium, dueDate, labelIds, projectId)
            is { } card ? Results.Ok(card) : Results.NotFound();
    }

    private static readonly HashSet<string> PatchFields =
        ["title", "description", "priority", "dueDate", "labels", "labelIds", "project", "projectId", "status", "index"];

    // A partial update: only the fields present change. Read as a JSON object (rather than a
    // record) to tell "absent" from "null", since a null dueDate clears it.
    private static async Task<IResult> PatchCard(long id, HttpRequest http, BoardStore store, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json)
    {
        JsonElement body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<JsonElement>(http.Body, cancellationToken: http.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return Results.Problem("The body must be a JSON object.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (body.ValueKind != JsonValueKind.Object)
            return Results.Problem("The body must be a JSON object.", statusCode: StatusCodes.Status400BadRequest);
        if (store.GetCard(id) is not { } card) return Results.NotFound();

        var errors = new Errors();
        var options = json.Value.SerializerOptions;
        T? Read<T>(string name)
        {
            try { return body.GetProperty(name).Deserialize<T>(options); }
            catch (JsonException) { errors.Add(name, $"Not a valid {name}."); return default; }
        }
        bool Has(string name) => body.TryGetProperty(name, out _);
        foreach (var property in body.EnumerateObject())
        {
            if (!PatchFields.Contains(property.Name)) errors.Add(property.Name, $"Unknown field. Fields: {string.Join(", ", PatchFields)}.");
        }

        var title = Has("title") ? errors.Title(Read<string>("title")) : card.Title;
        var description = Has("description") ? errors.Description(Read<string>("description")) : card.Description;
        var priority = card.Priority;
        if (Has("priority"))
        {
            if (Read<Priority?>("priority") is { } p) priority = p;
            else errors.Add("priority", "Use low, medium, high or urgent.");
        }
        var dueDate = Has("dueDate") ? errors.DueDate(Read<string>("dueDate")) : card.DueDate;
        IReadOnlyList<long> labelIds = card.LabelIds;
        if (Has("labelIds")) labelIds = errors.LabelIds(Read<long[]>("labelIds"), store);
        var labelNames = Has("labels") ? errors.LabelNames(Read<string[]>("labels")) : null;
        var projectId = card.ProjectId;
        if (Has("projectId")) projectId = errors.ProjectId(Read<long?>("projectId"), store);
        var projectName = Has("project") ? errors.ProjectName(Read<string>("project")) : null;
        if (Has("project") && Has("projectId")) errors.Add("project", "Send project or projectId, not both.");
        if (Has("project") && projectName is null) projectId = null;
        CardStatus? status = null;
        if (Has("status"))
        {
            if (Read<CardStatus?>("status") is { } st) status = st;
            else errors.Add("status", StatusHint);
        }
        int? index = Has("index") ? Read<int?>("index") : null;
        if (index is < 0) errors.Add("index", "Must be 0 or more.");
        if (index is not null && status is null) status = card.Status;
        if (errors.Any) return errors.Result();

        if (labelNames is not null)
        {
            var named = store.LabelIdsByName(labelNames);
            labelIds = Has("labelIds") ? [.. labelIds.Union(named)] : named;
        }
        if (projectName is not null) projectId = store.ProjectIdByName(projectName);
        var updated = store.UpdateCard(id, title, description, priority, dueDate, [.. labelIds], projectId);
        if (updated is not null && status is { } to && (to != card.Status || index is not null))
            updated = store.MoveCard(id, to, index);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    private static IResult AddComment(long id, CommentRequest request, BoardStore store)
    {
        var errors = new Errors();
        var body = request.Body?.Trim() ?? "";
        if (body.Length == 0) errors.Add("body", "A comment needs some text.");
        else if (body.Length > MaxDescription) errors.Add("body", $"Keep comments to {MaxDescription} characters.");
        var author = string.IsNullOrWhiteSpace(request.Author) ? "Tom" : request.Author.Trim();
        if (author.Length > MaxLabelName) errors.Add("author", $"Keep the author to {MaxLabelName} characters.");
        if (errors.Any) return errors.Result();

        return store.AddComment(id, author, body) is { } comment
            ? Results.Created($"/api/cards/{id}/comments", comment)
            : Results.NotFound();
    }

    private static IResult MoveCard(long id, MoveCardRequest request, BoardStore store)
    {
        var errors = new Errors();
        if (request.Status is null) errors.Add("status", "Choose a column.");
        if (request.Index is < 0) errors.Add("index", "Must be 0 or more.");
        if (errors.Any) return errors.Result();

        return store.MoveCard(id, request.Status!.Value, request.Index) is { } card
            ? Results.Ok(card)
            : Results.NotFound();
    }

    private static IResult CreateLabel(LabelRequest request, BoardStore store)
    {
        var errors = new Errors();
        var (name, color) = errors.Named(request.Name, request.Color, "label", MaxLabelName);
        if (errors.Any) return errors.Result();

        return store.CreateLabel(name, color) switch
        {
            (Outcome.Ok, { } label) => Results.Created($"/api/labels/{label.Id}", label),
            _ => NameTaken("label", name),
        };
    }

    private static IResult UpdateLabel(long id, LabelRequest request, BoardStore store)
    {
        var errors = new Errors();
        var (name, color) = errors.Named(request.Name, request.Color, "label", MaxLabelName);
        if (errors.Any) return errors.Result();

        return store.UpdateLabel(id, name, color) switch
        {
            (Outcome.Ok, { } label) => Results.Ok(label),
            (Outcome.NotFound, _) => Results.NotFound(),
            _ => NameTaken("label", name),
        };
    }

    private static IResult CreateProject(ProjectRequest request, BoardStore store)
    {
        var errors = new Errors();
        var (name, color) = errors.Named(request.Name, request.Color, "project", MaxProjectName);
        if (errors.Any) return errors.Result();

        return store.CreateProject(name, color) switch
        {
            (Outcome.Ok, { } project) => Results.Created($"/api/projects/{project.Id}", project),
            _ => NameTaken("project", name),
        };
    }

    private static IResult UpdateProject(long id, ProjectRequest request, BoardStore store)
    {
        var errors = new Errors();
        var (name, color) = errors.Named(request.Name, request.Color, "project", MaxProjectName);
        if (errors.Any) return errors.Result();

        return store.UpdateProject(id, name, color) switch
        {
            (Outcome.Ok, { } project) => Results.Ok(project),
            (Outcome.NotFound, _) => Results.NotFound(),
            _ => NameTaken("project", name),
        };
    }

    private static IResult NameTaken(string kind, string name) =>
        Results.Problem($"There's already a {kind} called \"{name}\".", statusCode: StatusCodes.Status409Conflict);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    /// <summary>Collects validation errors per field, in the shape <c>Results.ValidationProblem</c> wants.</summary>
    private sealed class Errors
    {
        private readonly Dictionary<string, List<string>> _errors = [];

        public bool Any => _errors.Count > 0;

        public void Add(string field, string message)
        {
            if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
            list.Add(message);
        }

        public IResult Result() =>
            Results.ValidationProblem(_errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));

        public string Title(string? value)
        {
            var title = value?.Trim() ?? "";
            if (title.Length == 0) Add("title", "A card needs a title.");
            else if (title.Length > MaxTitle) Add("title", $"Keep the title to {MaxTitle} characters.");
            return title;
        }

        public string Description(string? value)
        {
            var description = value ?? "";
            if (description.Length > MaxDescription)
                Add("description", $"Keep the description to {MaxDescription} characters.");
            return description;
        }

        /// <summary>A yyyy-MM-dd day, or null for none (empty counts as none).</summary>
        public string? DueDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Add("dueDate", "Use a date like 2026-11-30.");
            return null;
        }

        public long[] LabelIds(long[]? value, BoardStore store)
        {
            var ids = (value ?? []).Distinct().ToArray();
            var missing = store.MissingLabels(ids);
            if (missing.Count > 0) Add("labelIds", $"No label with id {string.Join(", ", missing)}.");
            return ids;
        }

        /// <summary>Label names, trimmed; each 1 to <see cref="MaxLabelName"/> characters.</summary>
        public string[] LabelNames(string[]? value)
        {
            var names = (value ?? []).Select(n => n?.Trim() ?? "").ToArray();
            if (names.Any(n => n.Length == 0 || n.Length > MaxLabelName))
                Add("labels", $"Label names need 1 to {MaxLabelName} characters.");
            return [.. names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        public long? ProjectId(long? value, BoardStore store)
        {
            if (value is { } id && !store.ProjectExists(id)) Add("projectId", $"No project with id {id}.");
            return value;
        }

        /// <summary>A project's name, trimmed, or null for none (empty counts as none).</summary>
        public string? ProjectName(string? value)
        {
            var name = value?.Trim() ?? "";
            if (name.Length > MaxProjectName) Add("project", $"Keep project names to {MaxProjectName} characters.");
            return name.Length == 0 ? null : name;
        }

        /// <summary>A label's or project's name and colour.</summary>
        public (string Name, string Color) Named(string? nameValue, string? colorValue, string kind, int maxName)
        {
            var name = nameValue?.Trim() ?? "";
            if (name.Length == 0) Add("name", $"A {kind} needs a name.");
            else if (name.Length > maxName) Add("name", $"Keep {kind} names to {maxName} characters.");

            var color = colorValue?.Trim() ?? "";
            if (!HexColor().IsMatch(color)) Add("color", "Use a colour like #3d6fa8.");
            return (name, color.ToLowerInvariant());
        }
    }
}
