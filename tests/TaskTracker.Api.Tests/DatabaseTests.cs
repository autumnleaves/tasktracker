namespace TaskTracker.Api.Tests;

public sealed class DatabaseTests
{
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
