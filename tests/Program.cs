using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

var projectRoot = FindProject();
var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
var serverDll = Path.Combine(projectRoot, "bin", configuration, "net10.0", "Lab7.dll");
var temporaryRoot = Path.Combine(Path.GetTempPath(), "Lab7.Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryRoot);
var databasePath = Path.Combine(temporaryRoot, "tickets.db");
Process? server = null;
HttpClient? client = null;
var checks = 0;
var logs = new ConcurrentQueue<string>();

try
{
    await Start();
    foreach (var path in new[] { "/", "/index.html", "/app.js", "/style.css" })
    {
        using var response = await client!.GetAsync(path);
        Check(response.StatusCode == HttpStatusCode.OK, $"static {path}");
        Check((await response.Content.ReadAsStringAsync()).Length > 0, $"nonempty {path}");
    }

    var seeded = await Get("/api/tickets");
    Check(seeded.GetArrayLength() == 4, "initial seed");
    Check(seeded[0].GetProperty("event_id").GetInt64() == 7 && seeded[0].GetProperty("type").GetString() == "general", "list order and snake_case");
    Check((await Get("/api/tickets?event_id=7")).GetArrayLength() == 2, "event filter");
    Check((await Get("/api/tickets?event_id=99")).GetArrayLength() == 0, "empty filter");
    await Error(HttpMethod.Get, "/api/tickets?event_id=bad", null, 400, "Поле event_id заполнено неверно");
    await Error(HttpMethod.Get, "/api/tickets/0", null, 400, "Поле id заполнено неверно");
    await Error(HttpMethod.Get, "/api/tickets/999999", null, 404, "Билет не найден");
    await Error(HttpMethod.Get, "/api/unknown", null, 404, "Маршрут не найден");
    await Error(HttpMethod.Get, "/missing.css", null, 404, "Маршрут не найден");

    var ticket = await Send(HttpMethod.Post, "/api/tickets", new { event_id = "100", type = "VIP", price = "123.456", remaining = "5" }, 201);
    var id = ticket.GetProperty("id").GetInt64();
    var url = $"/api/tickets/{id}";
    Check(ticket.GetProperty("type").GetString() == "vip", "type normalization");
    Check(ticket.GetProperty("price").GetDouble() == 123.46, "price rounding and form strings");
    Check((await Get(url)).GetProperty("remaining").GetInt64() == 5, "get created");
    var updated = await Send(HttpMethod.Patch, url, new { price = 125.5 }, 200);
    Check(updated.GetProperty("event_id").GetInt64() == 100 && updated.GetProperty("remaining").GetInt64() == 5, "patch preserves fields");
    Check(updated.GetProperty("price").GetDouble() == 125.5, "patch changes price");

    await Error(HttpMethod.Post, "/api/tickets", new { event_id = 1, type = "other", price = 1, remaining = 1 }, 400, "Тип билета должен быть general или vip");
    await Error(HttpMethod.Post, "/api/tickets", new { event_id = 0, type = "general", price = 1, remaining = 1 }, 400, "Поле event_id заполнено неверно");
    await Error(HttpMethod.Post, "/api/tickets", new { event_id = 1, type = "general", price = -1, remaining = 1 }, 400, "Цена должна быть неотрицательным числом");
    await Error(HttpMethod.Post, "/api/tickets", new { event_id = 1, type = "general", price = 1, remaining = 0.5 }, 400, "Поле remaining заполнено неверно");
    await Error(HttpMethod.Patch, url, new { remaining = -1 }, 400, "Поле remaining заполнено неверно");
    await Error(HttpMethod.Post, url + "/purchase", new { quantity = 0 }, 400, "Поле quantity заполнено неверно");
    await Error(HttpMethod.Post, url + "/purchase", new { quantity = (object?)null }, 400, "Поле quantity заполнено неверно");

    var purchased = await Send(HttpMethod.Post, url + "/purchase", new { }, 200);
    Check(purchased.GetProperty("remaining").GetInt64() == 4, "default purchase quantity");
    await Error(HttpMethod.Post, url + "/purchase", new { quantity = 5 }, 400, "Недостаточно билетов в остатке");
    Check((await Get(url)).GetProperty("remaining").GetInt64() == 4, "failed purchase rollback");

    var purchases = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
    {
        using var response = await client!.PostAsJsonAsync(url + "/purchase", new { quantity = 1 });
        return (int)response.StatusCode;
    }));
    Check(purchases.Count(status => status == 200) == 4 && purchases.Count(status => status == 400) == 4, "concurrent purchases do not oversell");
    Check((await Get(url)).GetProperty("remaining").GetInt64() == 0, "final stock is zero");

    using (var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/tickets")
    {
        Content = new StringContent("{", Encoding.UTF8, "application/json")
    })
    using (var response = await client!.SendAsync(invalid))
    {
        Check((int)response.StatusCode == 400, "malformed JSON status");
        Check((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString() == "Некорректный JSON", "malformed JSON body");
    }

    Stop();
    await Start();
    Check((await Get(url)).GetProperty("price").GetDouble() == 125.5, "data persists after restart");
    Check((await Get(url)).GetProperty("remaining").GetInt64() == 0, "stock persists after restart");
    Check((await Get("/api/tickets")).GetArrayLength() == 5, "restart does not duplicate seed");
    using (var response = await client!.DeleteAsync(url))
        Check((int)response.StatusCode == 204 && (await response.Content.ReadAsByteArrayAsync()).Length == 0, "delete 204 empty body");
    await Error(HttpMethod.Get, url, null, 404, "Билет не найден");
    await Error(HttpMethod.Delete, url, null, 404, "Билет не найден");
    Console.WriteLine($"PASS: {checks} HTTP checks (CRUD, validation, transaction rollback, concurrency, persistence, static files).");
}
catch
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, logs));
    throw;
}
finally
{
    Stop();
    var resolvedRoot = Path.GetFullPath(temporaryRoot);
    if (resolvedRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(resolvedRoot).StartsWith("Lab7.Tests-", StringComparison.Ordinal))
        Directory.Delete(resolvedRoot, true);
}

void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
    checks++;
}

async Task<JsonElement> Get(string path) => await Send(HttpMethod.Get, path, null, 200);

async Task<JsonElement> Send(HttpMethod method, string path, object? body, int status)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null) request.Content = JsonContent.Create(body);
    using var response = await client!.SendAsync(request);
    Check((int)response.StatusCode == status, $"{method} {path}: expected {status}, got {(int)response.StatusCode}");
    return await response.Content.ReadFromJsonAsync<JsonElement>();
}

async Task Error(HttpMethod method, string path, object? body, int status, string message)
{
    var result = await Send(method, path, body, status);
    Check(result.GetProperty("error").GetString() == message, $"{method} {path}: error message");
}

async Task Start()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    var info = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = temporaryRoot,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    info.ArgumentList.Add(serverDll);
    info.ArgumentList.Add("--urls");
    info.ArgumentList.Add($"http://127.0.0.1:{port}");
    info.Environment["DATABASE_PATH"] = databasePath;
    info.Environment["Logging__LogLevel__Default"] = "Warning";
    server = Process.Start(info) ?? throw new InvalidOperationException("Server did not start");
    server.OutputDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
    server.ErrorDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
    server.BeginOutputReadLine();
    server.BeginErrorReadLine();
    client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(2) };
    for (var attempt = 0; attempt < 100; attempt++)
    {
        if (server.HasExited) throw new InvalidOperationException("Server exited: " + string.Join("\n", logs));
        try
        {
            using var response = await client.GetAsync("/api/tickets");
            if (response.IsSuccessStatusCode) return;
        }
        catch (HttpRequestException) { }
        await Task.Delay(100);
    }
    throw new TimeoutException("Server startup timed out");
}

void Stop()
{
    client?.Dispose();
    client = null;
    if (server is null) return;
    if (!server.HasExited) server.Kill(entireProcessTree: true);
    server.WaitForExit();
    server.Dispose();
    server = null;
}

static string FindProject()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "Lab7.csproj"))) return directory.FullName;
    throw new FileNotFoundException("Lab7.csproj not found");
}
