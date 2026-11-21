namespace TaskTracker.Api.Board;

// In JSON (and in the database) these are snake_case: "backlog", "todo", "in_progress", "done".
public enum CardStatus { Backlog, Todo, InProgress, Done }

public enum Priority { Low, Medium, High, Urgent }

public sealed record Column(CardStatus Id, string Name);

public sealed record Label(long Id, string Name, string Color);

public sealed record Card(
    long Id,
    string Title,
    string Description,
    CardStatus Status,
    Priority Priority,
    string? DueDate,
    long Position,
    IReadOnlyList<long> LabelIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Everything the board page shows, in one response.</summary>
public sealed record BoardView(IReadOnlyList<Column> Columns, IReadOnlyList<Card> Cards, IReadOnlyList<Label> Labels);

public sealed record CreateCardRequest(
    string? Title,
    string? Description = null,
    CardStatus? Status = null,
    Priority? Priority = null,
    string? DueDate = null,
    long[]? LabelIds = null);

/// <summary>Replaces a card's editable fields. Its column and order change through a move.</summary>
public sealed record UpdateCardRequest(
    string? Title,
    string? Description = null,
    Priority? Priority = null,
    string? DueDate = null,
    long[]? LabelIds = null);

/// <summary>Moves a card to <see cref="Index"/> in a column (0 is the top; past the end means the bottom).</summary>
public sealed record MoveCardRequest(CardStatus? Status, int? Index = null);

public sealed record LabelRequest(string? Name, string? Color);

public static class BoardColumns
{
    public static readonly IReadOnlyList<Column> All =
    [
        new(CardStatus.Backlog, "Backlog"),
        new(CardStatus.Todo, "To do"),
        new(CardStatus.InProgress, "In progress"),
        new(CardStatus.Done, "Done"),
    ];
}

/// <summary>The text stored in the database for each enum value: the same as in JSON.</summary>
internal static class Codes
{
    private static readonly Dictionary<CardStatus, string> StatusCodes = new()
    {
        [CardStatus.Backlog] = "backlog",
        [CardStatus.Todo] = "todo",
        [CardStatus.InProgress] = "in_progress",
        [CardStatus.Done] = "done",
    };

    private static readonly Dictionary<Priority, string> PriorityCodes = new()
    {
        [Priority.Low] = "low",
        [Priority.Medium] = "medium",
        [Priority.High] = "high",
        [Priority.Urgent] = "urgent",
    };

    private static readonly Dictionary<string, CardStatus> Statuses =
        StatusCodes.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly Dictionary<string, Priority> Priorities =
        PriorityCodes.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static string Code(this CardStatus status) => StatusCodes[status];
    public static string Code(this Priority priority) => PriorityCodes[priority];
    public static CardStatus ParseStatus(string code) => Statuses[code];
    public static Priority ParsePriority(string code) => Priorities[code];
}
