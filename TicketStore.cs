using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Lab7;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed record Ticket(
    long Id,
    [property: JsonPropertyName("event_id")] long EventId,
    string Type,
    double Price,
    long Remaining);

public sealed class TicketStore : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly object gate = new();

    public TicketStore(string filename)
    {
        if (filename != ":memory:")
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filename))!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = filename,
            Pooling = false
        }.ToString());
        connection.Open();
        Execute("PRAGMA journal_mode = WAL");
        Execute("""
            CREATE TABLE IF NOT EXISTS tickets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id INTEGER NOT NULL,
                type TEXT NOT NULL CHECK(type IN ('general', 'vip')),
                price REAL NOT NULL CHECK(price >= 0),
                remaining INTEGER NOT NULL
            )
            """);
        using var transaction = connection.BeginTransaction();
        using var count = Command("SELECT COUNT(*) FROM tickets", transaction);
        if ((long)count.ExecuteScalar()! == 0)
        {
            Execute("""
                INSERT INTO tickets (event_id, type, price, remaining) VALUES
                    (7, 'general', 1200, 34), (7, 'vip', 2800, 8),
                    (12, 'general', 900, 52), (12, 'vip', 2100, 10)
                """, transaction);
        }
        transaction.Commit();
    }

    public IReadOnlyList<Ticket> List(string? eventId)
    {
        lock (gate)
        {
            using var command = eventId is null
                ? Command("SELECT * FROM tickets ORDER BY event_id, type, id")
                : Command("SELECT * FROM tickets WHERE event_id = $event ORDER BY type, id");
            if (eventId is not null)
                command.Parameters.AddWithValue("$event", ParseInteger(eventId, "event_id", 1));
            using var reader = command.ExecuteReader();
            var tickets = new List<Ticket>();
            while (reader.Read()) tickets.Add(Read(reader));
            return tickets;
        }
    }

    public Ticket Get(string id)
    {
        lock (gate) return Find(ParseInteger(id, "id", 1));
    }

    public Ticket Create(JsonElement input)
    {
        var ticket = Validate(input);
        lock (gate)
        {
            using var command = Command("""
                INSERT INTO tickets (event_id, type, price, remaining)
                VALUES ($event, $type, $price, $remaining);
                SELECT last_insert_rowid();
                """);
            Bind(command, ticket);
            return Find((long)command.ExecuteScalar()!);
        }
    }

    public Ticket Update(string id, JsonElement input)
    {
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            var current = Find(ParseInteger(id, "id", 1), transaction);
            var ticket = Validate(input, current);
            using var command = Command("""
                UPDATE tickets SET event_id = $event, type = $type, price = $price, remaining = $remaining
                WHERE id = $id
                """, transaction);
            Bind(command, ticket);
            command.Parameters.AddWithValue("$id", current.Id);
            command.ExecuteNonQuery();
            var updated = Find(current.Id, transaction);
            transaction.Commit();
            return updated;
        }
    }

    public void Remove(string id)
    {
        lock (gate)
        {
            using var command = Command("DELETE FROM tickets WHERE id = $id");
            command.Parameters.AddWithValue("$id", ParseInteger(id, "id", 1));
            if (command.ExecuteNonQuery() == 0) throw new ApiException(404, "Билет не найден");
        }
    }

    public Ticket Purchase(string id, JsonElement input)
    {
        var ticketId = ParseInteger(id, "id", 1);
        var amount = input.TryGetProperty("quantity", out var quantity)
            ? ParseInteger(quantity, "quantity", 1) : 1;
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            _ = Find(ticketId, transaction);
            using var command = Command("UPDATE tickets SET remaining = remaining - $amount WHERE id = $id", transaction);
            command.Parameters.AddWithValue("$amount", amount);
            command.Parameters.AddWithValue("$id", ticketId);
            command.ExecuteNonQuery();
            var ticket = Find(ticketId, transaction);
            if (ticket.Remaining < 0) throw new ApiException(400, "Недостаточно билетов в остатке");
            transaction.Commit();
            return ticket;
        }
    }

    private Ticket Find(long id, SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT * FROM tickets WHERE id = $id", transaction);
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new ApiException(404, "Билет не найден");
        return Read(reader);
    }

    private static Ticket Read(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetDouble(3), reader.GetInt64(4));

    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    private void Execute(string sql, SqliteTransaction? transaction = null)
    {
        using var command = Command(sql, transaction);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, Ticket ticket)
    {
        command.Parameters.AddWithValue("$event", ticket.EventId);
        command.Parameters.AddWithValue("$type", ticket.Type);
        command.Parameters.AddWithValue("$price", ticket.Price);
        command.Parameters.AddWithValue("$remaining", ticket.Remaining);
    }

    private static Ticket Validate(JsonElement input, Ticket? current = null)
    {
        var type = input.TryGetProperty("type", out var typeValue)
            ? typeValue.ToString().ToLowerInvariant() : current?.Type ?? "";
        if (type is not ("general" or "vip"))
            throw new ApiException(400, "Тип билета должен быть general или vip");
        var eventId = input.TryGetProperty("event_id", out var eventValue)
            ? ParseInteger(eventValue, "event_id", 1)
            : current?.EventId ?? throw new ApiException(400, "Поле event_id заполнено неверно");
        var price = input.TryGetProperty("price", out var priceValue) ? Number(priceValue) : current?.Price ?? double.NaN;
        if (!double.IsFinite(price) || price < 0 || !double.IsFinite(price * 100))
            throw new ApiException(400, "Цена должна быть неотрицательным числом");
        price = Math.Floor(price * 100 + 0.5) / 100;
        var remaining = input.TryGetProperty("remaining", out var remainingValue)
            ? ParseInteger(remainingValue, "remaining", 0)
            : current?.Remaining ?? throw new ApiException(400, "Поле remaining заполнено неверно");
        return new Ticket(current?.Id ?? 0, eventId, type, price, remaining);
    }

    private static long ParseInteger(JsonElement value, string name, long minimum) =>
        Integer(Number(value), name, minimum);

    private static long ParseInteger(string value, string name, long minimum) =>
        Integer(Number(value), name, minimum);

    private static long Integer(double value, string name, long minimum)
    {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < minimum || value >= long.MaxValue)
            throw new ApiException(400, $"Поле {name} заполнено неверно");
        return (long)value;
    }

    private static double Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => 0,
        JsonValueKind.True => 1,
        JsonValueKind.Number => value.TryGetDouble(out var number) ? number : double.NaN,
        JsonValueKind.String => Number(value.GetString()!),
        _ => double.NaN
    };

    private static double Number(string value) => string.IsNullOrWhiteSpace(value) ? 0
        : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN;

    public void Dispose() => connection.Dispose();
}
