using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using TaskTracker.Api.Data;

namespace TaskTracker.Api.Board;

public enum Outcome { Ok, NotFound, NameTaken }

/// <summary>Reads and writes cards, labels and projects. Callers validate input first (BoardApi).</summary>
public sealed class BoardStore(Database db, TimeProvider time)
{
    private const string CardColumns =
        "id AS Id, title AS Title, description AS Description, status AS Status, priority AS Priority, " +
        "due_date AS DueDate, position AS Position, created_at AS CreatedAt, updated_at AS UpdatedAt, " +
        "completed_at AS CompletedAt, project_id AS ProjectId";

    // Labels and projects are both a name (unique, ignoring case) and a colour, in these tables.
    private const string LabelsTable = "labels";
    private const string ProjectsTable = "projects";
    private const string NamedColumns = "id AS Id, name AS Name, color AS Color";

    private const int SqliteConstraint = 19;

    public void Ping()
    {
        using var c = db.Open();
        c.ExecuteScalar<long>("SELECT 1");
    }

    public BoardView GetBoard()
    {
        using var c = db.Open();
        var cards = LoadCards(c, "", null)
            .OrderBy(card => card.Status)
            .ThenBy(card => card.Position)
            .ThenBy(card => card.Id)
            .ToList();
        return new BoardView(
            BoardColumns.All, cards, ListNamed(c, LabelsTable, ToLabel), ListNamed(c, ProjectsTable, ToProject));
    }

    public Card? GetCard(long id)
    {
        using var c = db.Open();
        return Load(c, id);
    }

    /// <summary>The ids in <paramref name="ids"/> that aren't labels.</summary>
    public IReadOnlyList<long> MissingLabels(IEnumerable<long> ids)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0) return [];
        using var c = db.Open();
        var found = c.Query<long>("SELECT id FROM labels WHERE id IN @wanted", new { wanted }).ToHashSet();
        return [.. wanted.Where(id => !found.Contains(id))];
    }

    public bool ProjectExists(long id)
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM projects WHERE id = @id", new { id }) > 0;
    }

    /// <summary>Adds a card at the bottom of its column.</summary>
    public Card CreateCard(
        string title, string description, CardStatus status, Priority priority, string? dueDate,
        IReadOnlyCollection<long> labelIds, long? projectId)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var now = Now();
        var position = c.ExecuteScalar<long>(
            "SELECT COALESCE(MAX(position), 0) + 1 FROM cards WHERE status = @status",
            new { status = status.Code() }, tx);
        var id = c.ExecuteScalar<long>(
            """
            INSERT INTO cards (title, description, status, priority, due_date, position, created_at, updated_at, completed_at, project_id)
            VALUES (@title, @description, @status, @priority, @dueDate, @position, @now, @now, @completedAt, @projectId)
            RETURNING id
            """,
            new
            {
                title,
                description,
                status = status.Code(),
                priority = priority.Code(),
                dueDate,
                position,
                now,
                completedAt = status == CardStatus.Done ? now : null,
                projectId,
            },
            tx);
        SetLabels(c, tx, id, labelIds);
        tx.Commit();
        return Load(c, id)!;
    }

    public Card? UpdateCard(
        long id, string title, string description, Priority priority, string? dueDate,
        IReadOnlyCollection<long> labelIds, long? projectId)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var updated = c.Execute(
            """
            UPDATE cards
            SET title = @title, description = @description, priority = @priority, due_date = @dueDate,
                project_id = @projectId, updated_at = @now
            WHERE id = @id
            """,
            new { id, title, description, priority = priority.Code(), dueDate, projectId, now = Now() },
            tx);
        if (updated == 0) return null;
        SetLabels(c, tx, id, labelIds);
        tx.Commit();
        return Load(c, id);
    }

    /// <summary>
    /// Puts a card at <paramref name="index"/> in <paramref name="status"/>'s column (the bottom when
    /// null or past the end) and renumbers that column. Moving into Done sets the completion time;
    /// moving out clears it.
    /// </summary>
    public Card? MoveCard(long id, CardStatus status, int? index)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var current = c.QuerySingleOrDefault<string>("SELECT status FROM cards WHERE id = @id", new { id }, tx);
        if (current is null) return null;

        var order = c.Query<long>(
            "SELECT id FROM cards WHERE status = @status AND id <> @id ORDER BY position, id",
            new { status = status.Code(), id }, tx).ToList();
        order.Insert(Math.Clamp(index ?? order.Count, 0, order.Count), id);
        c.Execute(
            "UPDATE cards SET position = @position WHERE id = @id",
            order.Select((cardId, i) => new { id = cardId, position = i + 1L }),
            tx);

        if (Codes.ParseStatus(current) != status)
        {
            var now = Now();
            c.Execute(
                """
                UPDATE cards
                SET status = @status, updated_at = @now, completed_at = CASE WHEN @done THEN @now ELSE NULL END
                WHERE id = @id
                """,
                new { id, status = status.Code(), now, done = status == CardStatus.Done },
                tx);
        }
        tx.Commit();
        return Load(c, id);
    }

    public bool DeleteCard(long id)
    {
        using var c = db.Open();
        return c.Execute("DELETE FROM cards WHERE id = @id", new { id }) > 0;
    }

    public IReadOnlyList<Label> GetLabels()
    {
        using var c = db.Open();
        return ListNamed(c, LabelsTable, ToLabel);
    }

    public (Outcome, Label?) CreateLabel(string name, string color) => CreateNamed(LabelsTable, name, color, ToLabel);

    public (Outcome, Label?) UpdateLabel(long id, string name, string color) =>
        UpdateNamed(LabelsTable, id, name, color, ToLabel);

    public bool DeleteLabel(long id) => DeleteNamed(LabelsTable, id);

    /// <summary>
    /// The ids of labels with these names (ignoring case), making any that don't exist yet with
    /// the next unused colour.
    /// </summary>
    public IReadOnlyList<long> LabelIdsByName(IEnumerable<string> names) => IdsByName(LabelsTable, names);

    public IReadOnlyList<Project> GetProjects()
    {
        using var c = db.Open();
        return ListNamed(c, ProjectsTable, ToProject);
    }

    public (Outcome, Project?) CreateProject(string name, string color) => CreateNamed(ProjectsTable, name, color, ToProject);

    public (Outcome, Project?) UpdateProject(long id, string name, string color) =>
        UpdateNamed(ProjectsTable, id, name, color, ToProject);

    /// <summary>Deletes a project; its cards are left without one.</summary>
    public bool DeleteProject(long id) => DeleteNamed(ProjectsTable, id);

    /// <summary>The id of the project with this name (ignoring case), made with the next unused colour if new.</summary>
    public long ProjectIdByName(string name) => IdsByName(ProjectsTable, [name]).Single();

    // `table` is always LabelsTable or ProjectsTable, never input.
    private static List<T> ListNamed<T>(SqliteConnection c, string table, Func<NamedRow, T> make) =>
        [.. c.Query<NamedRow>($"SELECT {NamedColumns} FROM {table} ORDER BY name").Select(make)];

    private (Outcome, T?) CreateNamed<T>(string table, string name, string color, Func<NamedRow, T> make)
        where T : class
    {
        using var c = db.Open();
        try
        {
            var id = c.ExecuteScalar<long>(
                $"INSERT INTO {table} (name, color) VALUES (@name, @color) RETURNING id", new { name, color });
            return (Outcome.Ok, make(new NamedRow { Id = id, Name = name, Color = color }));
        }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint)
        {
            return (Outcome.NameTaken, null);
        }
    }

    private (Outcome, T?) UpdateNamed<T>(string table, long id, string name, string color, Func<NamedRow, T> make)
        where T : class
    {
        using var c = db.Open();
        try
        {
            var updated = c.Execute(
                $"UPDATE {table} SET name = @name, color = @color WHERE id = @id", new { id, name, color });
            return updated == 0
                ? (Outcome.NotFound, null)
                : (Outcome.Ok, make(new NamedRow { Id = id, Name = name, Color = color }));
        }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint)
        {
            return (Outcome.NameTaken, null);
        }
    }

    private bool DeleteNamed(string table, long id)
    {
        using var c = db.Open();
        return c.Execute($"DELETE FROM {table} WHERE id = @id", new { id }) > 0;
    }

    private IReadOnlyList<long> IdsByName(string table, IEnumerable<string> names)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var ids = new List<long>();
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var id = c.QuerySingleOrDefault<long?>(
                $"SELECT id FROM {table} WHERE name = @name COLLATE NOCASE", new { name }, tx);
            if (id is null)
            {
                var used = c.Query<string>($"SELECT color FROM {table}", transaction: tx).ToHashSet();
                var color = Palette.FirstOrDefault(col => !used.Contains(col)) ?? Palette[used.Count % Palette.Length];
                id = c.ExecuteScalar<long>(
                    $"INSERT INTO {table} (name, color) VALUES (@name, @color) RETURNING id", new { name, color }, tx);
            }
            ids.Add(id.Value);
        }
        tx.Commit();
        return ids;
    }

    // The web app's palette (web/src/LabelChip.tsx), for labels and projects made by name.
    private static readonly string[] Palette =
        ["#3d6fa8", "#2f8f6b", "#b4690e", "#8a4fb5", "#c8323c", "#3f8a9c", "#7d6b22", "#66707a"];

    /// <summary>A card's comments, oldest first, or null if there's no such card.</summary>
    public IReadOnlyList<Comment>? GetComments(long cardId)
    {
        using var c = db.Open();
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM cards WHERE id = @cardId", new { cardId }) == 0) return null;
        return
        [
            .. c.Query<CommentRow>(
                    $"SELECT {CommentColumns} FROM comments WHERE card_id = @cardId ORDER BY id", new { cardId })
                .Select(ToComment),
        ];
    }

    /// <summary>Adds a comment, or returns null if there's no such card.</summary>
    public Comment? AddComment(long cardId, string author, string body)
    {
        using var c = db.Open();
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM cards WHERE id = @cardId", new { cardId }) == 0) return null;
        var now = Now();
        var id = c.ExecuteScalar<long>(
            "INSERT INTO comments (card_id, author, body, created_at) VALUES (@cardId, @author, @body, @now) RETURNING id",
            new { cardId, author, body, now });
        return new Comment(id, cardId, author, body, ParseTime(now));
    }

    public bool DeleteComment(long id)
    {
        using var c = db.Open();
        return c.Execute("DELETE FROM comments WHERE id = @id", new { id }) > 0;
    }

    private const string CommentColumns =
        "id AS Id, card_id AS CardId, author AS Author, body AS Body, created_at AS CreatedAt";

    private static Comment ToComment(CommentRow r) => new(r.Id, r.CardId, r.Author, r.Body, ParseTime(r.CreatedAt));

    private sealed class CommentRow
    {
        public long Id { get; init; }
        public long CardId { get; init; }
        public string Author { get; init; } = "";
        public string Body { get; init; } = "";
        public string CreatedAt { get; init; } = "";
    }

    private static void SetLabels(SqliteConnection c, SqliteTransaction tx, long cardId, IReadOnlyCollection<long> labelIds)
    {
        c.Execute("DELETE FROM card_labels WHERE card_id = @cardId", new { cardId }, tx);
        c.Execute(
            "INSERT INTO card_labels (card_id, label_id) VALUES (@cardId, @labelId)",
            labelIds.Distinct().Select(labelId => new { cardId, labelId }),
            tx);
    }

    private static Card? Load(SqliteConnection c, long id) =>
        LoadCards(c, "WHERE id = @id", new { id }).SingleOrDefault();

    // Cards with their labels (ids and names), comment counts and project names. `where` filters the cards table.
    private static List<Card> LoadCards(SqliteConnection c, string where, object? args)
    {
        var rows = c.Query<CardRow>($"SELECT {CardColumns} FROM cards {where}", args).ToList();
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.Id).ToList();
        var links = c.Query<(long CardId, long LabelId, string Name)>(
                """
                SELECT cl.card_id, cl.label_id, l.name
                FROM card_labels cl JOIN labels l ON l.id = cl.label_id
                WHERE cl.card_id IN @ids
                ORDER BY cl.label_id
                """, new { ids })
            .ToLookup(l => l.CardId);
        var comments = c.Query<(long CardId, long Count)>(
                "SELECT card_id, COUNT(*) FROM comments WHERE card_id IN @ids GROUP BY card_id", new { ids })
            .ToDictionary(x => x.CardId, x => (int)x.Count);
        var projectIds = rows.Where(r => r.ProjectId is not null).Select(r => r.ProjectId!.Value).Distinct().ToList();
        var projects = projectIds.Count == 0
            ? []
            : c.Query<(long Id, string Name)>("SELECT id, name FROM projects WHERE id IN @projectIds", new { projectIds })
                .ToDictionary(p => p.Id, p => p.Name);
        return
        [
            .. rows.Select(r => ToCard(
                r,
                [.. links[r.Id].Select(l => l.LabelId)],
                [.. links[r.Id].Select(l => l.Name)],
                comments.GetValueOrDefault(r.Id),
                r.ProjectId is { } pid ? projects.GetValueOrDefault(pid) : null)),
        ];
    }

    private string Now() => time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static Card ToCard(
        CardRow r, IReadOnlyList<long> labelIds, IReadOnlyList<string> labels, int comments, string? project) => new(
        r.Id,
        r.Title,
        r.Description,
        Codes.ParseStatus(r.Status),
        Codes.ParsePriority(r.Priority),
        r.DueDate,
        r.Position,
        labelIds,
        ParseTime(r.CreatedAt),
        ParseTime(r.UpdatedAt),
        r.CompletedAt is null ? null : ParseTime(r.CompletedAt),
        labels,
        comments,
        project is null ? null : r.ProjectId,
        project);

    private static Label ToLabel(NamedRow r) => new(r.Id, r.Name, r.Color);

    private static Project ToProject(NamedRow r) => new(r.Id, r.Name, r.Color);

    private sealed class CardRow
    {
        public long Id { get; init; }
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
        public string Status { get; init; } = "";
        public string Priority { get; init; } = "";
        public string? DueDate { get; init; }
        public long Position { get; init; }
        public string CreatedAt { get; init; } = "";
        public string UpdatedAt { get; init; } = "";
        public string? CompletedAt { get; init; }
        public long? ProjectId { get; init; }
    }

    private sealed class NamedRow
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";
        public string Color { get; init; } = "";
    }
}
