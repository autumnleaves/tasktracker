using System.Net.Http.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using TaskTracker.Api.Board;
using TaskTracker.Api.Data;

namespace TaskTracker.Api.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task Upgrading_rebuilds_the_cards_table_and_keeps_everything()
    {
        // A database as the release before In review left it: schema version 3.
        var app = new TestApp();
        try
        {
            using (var c = new SqliteConnection($"Data Source={app.DbPath};Pooling=False"))
            {
                c.Open();
                c.Execute("PRAGMA foreign_keys = ON;");
                foreach (var version in new[] { "0001_initial", "0002_comments", "0003_projects" })
                    c.Execute(Migration(version));
                c.Execute(
                    """
                    PRAGMA user_version = 3;
                    INSERT INTO projects (name, color) VALUES ('Homelab', '#3d6fa8');
                    INSERT INTO labels (name, color) VALUES ('network', '#2f8f6b');
                    INSERT INTO cards (title, status, position, created_at, updated_at, project_id)
                    VALUES ('Tunnel', 'in_progress', 1, '2026-11-01T00:00:00Z', '2026-11-01T00:00:00Z', 1),
                           ('Deleted later', 'todo', 1, '2026-11-01T00:00:00Z', '2026-11-01T00:00:00Z', NULL);
                    INSERT INTO card_labels (card_id, label_id) VALUES (1, 1);
                    INSERT INTO comments (card_id, author, body, created_at) VALUES (1, 'Tom', 'Started', '2026-11-01T00:00:00Z');
                    DELETE FROM cards WHERE id = 2;
                    """);
            }

            var client = app.CreateClient();
            var card = await client.GetFromJsonAsync<Card>("/api/cards/1", TestApp.Json);
            Assert.Equal("Tunnel", card!.Title);
            Assert.Equal("Homelab", card.Project);
            Assert.Equal(new[] { "network" }, card.Labels);
            Assert.Equal(1, card.CommentCount);

            var moved = await app.Move(1, "in_review");
            Assert.Equal(CardStatus.InReview, moved.Status);
            // #2 was deleted before the upgrade: its number still isn't reused.
            Assert.Equal(3, (await app.Create(new { title = "Next" })).Id);
            Assert.True(Directory.Exists(Path.Combine(app.Folder, "backups")));

            // Foreign keys are back on: deleting a card still deletes its comments.
            await client.DeleteAsync("/api/cards/1");
            using var check = new SqliteConnection($"Data Source={app.DbPath};Pooling=False");
            Assert.Equal(0, check.ExecuteScalar<long>("SELECT COUNT(*) FROM comments"));
            Assert.Equal(0, check.ExecuteScalar<long>("SELECT COUNT(*) FROM card_labels"));
        }
        finally
        {
            app.Dispose();
            app.DeleteFolder();
        }
    }

    private static string Migration(string name)
    {
        using var stream = typeof(Database).Assembly.GetManifestResourceStream($"migrations/{name}.sql")!;
        return new StreamReader(stream).ReadToEnd();
    }

    [Fact]
    public async Task Restarting_keeps_the_data_and_doesnt_migrate_again()
    {
        var first = new TestApp();
        try
        {
            var card = await first.Create(new { title = "Survives a restart" });
            first.Dispose();

            using var second = new TestApp(first.Folder);
            var board = await second.Board();

            Assert.Contains(board.Cards, c => c.Id == card.Id && c.Title == "Survives a restart");
            // Backups are only taken before a migration, and there was none to apply.
            Assert.False(Directory.Exists(Path.Combine(first.Folder, "backups")));
        }
        finally
        {
            first.DeleteFolder();
        }
    }
}
