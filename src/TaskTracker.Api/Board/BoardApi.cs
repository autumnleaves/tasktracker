using System.Globalization;
using System.Text.RegularExpressions;

namespace TaskTracker.Api.Board;

/// <summary>The HTTP API under <c>/api</c>. Validation lives here; storage in <see cref="BoardStore"/>.</summary>
public static partial class BoardApi
{
    public const int MaxTitle = 200;
    public const int MaxDescription = 20_000;
    public const int MaxLabelName = 40;

    public static RouteGroupBuilder MapBoardApi(this RouteGroupBuilder api)
    {
        api.MapGet("/health", (BoardStore store) =>
        {
            store.Ping();
            return Results.Ok(new { status = "ok" });
        });

        api.MapGet("/board", (BoardStore store) => store.GetBoard());

        api.MapPost("/cards", CreateCard);
        api.MapGet("/cards/{id:long}", (long id, BoardStore store) =>
            store.GetCard(id) is { } card ? Results.Ok(card) : Results.NotFound());
        api.MapPut("/cards/{id:long}", UpdateCard);
        api.MapPost("/cards/{id:long}/move", MoveCard);
        api.MapDelete("/cards/{id:long}", (long id, BoardStore store) =>
            store.DeleteCard(id) ? Results.NoContent() : Results.NotFound());

        api.MapGet("/labels", (BoardStore store) => store.GetLabels());
        api.MapPost("/labels", CreateLabel);
        api.MapPut("/labels/{id:long}", UpdateLabel);
        api.MapDelete("/labels/{id:long}", (long id, BoardStore store) =>
            store.DeleteLabel(id) ? Results.NoContent() : Results.NotFound());

        // Anything else under /api is a 404, not the web app's index.html.
        api.MapFallback(() => Results.NotFound());
        return api;
    }

    private static IResult CreateCard(CreateCardRequest request, BoardStore store)
    {
        var errors = new Errors();
        var title = errors.Title(request.Title);
        var description = errors.Description(request.Description);
        var dueDate = errors.DueDate(request.DueDate);
        var labelIds = errors.LabelIds(request.LabelIds, store);
        if (errors.Any) return errors.Result();

        var card = store.CreateCard(
            title, description, request.Status ?? CardStatus.Backlog, request.Priority ?? Priority.Medium,
            dueDate, labelIds);
        return Results.Created($"/api/cards/{card.Id}", card);
    }

    private static IResult UpdateCard(long id, UpdateCardRequest request, BoardStore store)
    {
        var errors = new Errors();
        var title = errors.Title(request.Title);
        var description = errors.Description(request.Description);
        var dueDate = errors.DueDate(request.DueDate);
        var labelIds = errors.LabelIds(request.LabelIds, store);
        if (errors.Any) return errors.Result();

        return store.UpdateCard(id, title, description, request.Priority ?? Priority.Medium, dueDate, labelIds)
            is { } card ? Results.Ok(card) : Results.NotFound();
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
        var (name, color) = errors.Label(request);
        if (errors.Any) return errors.Result();

        return store.CreateLabel(name, color) switch
        {
            (Outcome.Ok, { } label) => Results.Created($"/api/labels/{label.Id}", label),
            _ => NameTaken(name),
        };
    }

    private static IResult UpdateLabel(long id, LabelRequest request, BoardStore store)
    {
        var errors = new Errors();
        var (name, color) = errors.Label(request);
        if (errors.Any) return errors.Result();

        return store.UpdateLabel(id, name, color) switch
        {
            (Outcome.Ok, { } label) => Results.Ok(label),
            (Outcome.NotFound, _) => Results.NotFound(),
            _ => NameTaken(name),
        };
    }

    private static IResult NameTaken(string name) =>
        Results.Problem($"There's already a label called \"{name}\".", statusCode: StatusCodes.Status409Conflict);

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

        public (string Name, string Color) Label(LabelRequest request)
        {
            var name = request.Name?.Trim() ?? "";
            if (name.Length == 0) Add("name", "A label needs a name.");
            else if (name.Length > MaxLabelName) Add("name", $"Keep label names to {MaxLabelName} characters.");

            var color = request.Color?.Trim() ?? "";
            if (!HexColor().IsMatch(color)) Add("color", "Use a colour like #3d6fa8.");
            return (name, color.ToLowerInvariant());
        }
    }
}
