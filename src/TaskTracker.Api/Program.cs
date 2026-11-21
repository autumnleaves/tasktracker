using System.Text.Json;
using System.Text.Json.Serialization;
using TaskTracker.Api.Board;
using TaskTracker.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new Database(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Tasks")
    ?? throw new InvalidOperationException("ConnectionStrings:Tasks isn't set")));
builder.Services.AddSingleton<BoardStore>();

var app = builder.Build();

app.Services.GetRequiredService<Database>().Migrate(app.Logger);

app.UseExceptionHandler();
app.UseStatusCodePages();

// The built web app (web/dist), copied into wwwroot by the Dockerfile. In development the
// Vite dev server serves it instead and proxies /api here.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGroup("/api").MapBoardApi();
app.MapFallbackToFile("index.html");

app.Run();
