using System.Text.Json;
using Lab7;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = "public"
});

if (string.IsNullOrEmpty(builder.Configuration["urls"]))
{
    var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var value) && value is > 0 and <= 65535
        ? value : 3000;
    builder.WebHost.UseUrls($"http://localhost:{port}");
}

var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
var dataRoot = File.Exists(Path.Combine(projectRoot, "Lab7.csproj")) ? projectRoot : AppContext.BaseDirectory;
var databasePath = Environment.GetEnvironmentVariable("DATABASE_PATH")
    ?? Path.Combine(dataRoot, "data", "tickets.db");
builder.Services.AddSingleton(_ => new TicketStore(databasePath));
var app = builder.Build();
// Initialize schema before accepting requests, and dispose the connection on shutdown.
_ = app.Services.GetRequiredService<TicketStore>();

app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (ApiException exception)
    {
        context.Response.StatusCode = exception.Status;
        await context.Response.WriteAsJsonAsync(new { error = exception.Message });
    }
    catch (JsonException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = "Некорректный JSON" });
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Ошибка обработки запроса");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "Внутренняя ошибка сервера" });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.MapGet("/api/tickets", (HttpRequest request, TicketStore store) =>
    Results.Json(store.List(request.Query.ContainsKey("event_id") ? request.Query["event_id"].ToString() : null)));
app.MapGet("/api/tickets/{id}", (string id, TicketStore store) => Results.Json(store.Get(id)));
app.MapPost("/api/tickets", async (HttpRequest request, TicketStore store) =>
    Results.Json(store.Create(await ReadBody(request)), statusCode: 201));
app.MapPatch("/api/tickets/{id}", async (string id, HttpRequest request, TicketStore store) =>
    Results.Json(store.Update(id, await ReadBody(request))));
app.MapPost("/api/tickets/{id}/purchase", async (string id, HttpRequest request, TicketStore store) =>
    Results.Json(store.Purchase(id, await ReadBody(request))));
app.MapDelete("/api/tickets/{id}", (string id, TicketStore store) =>
{
    store.Remove(id);
    return Results.NoContent();
});
app.MapFallback("{**path}", () => Results.Json(new { error = "Маршрут не найден" }, statusCode: 404));
app.Run();

static async Task<JsonElement> ReadBody(HttpRequest request)
{
    if (request.ContentLength == 0 || (request.ContentLength is null && !request.Headers.ContainsKey("Transfer-Encoding")))
        return JsonSerializer.SerializeToElement(new { });
    using var document = await JsonDocument.ParseAsync(request.Body);
    if (document.RootElement.ValueKind != JsonValueKind.Object)
        throw new ApiException(400, "Тело запроса должно быть объектом JSON");
    return document.RootElement.Clone();
}
