const fs = require("node:fs");
const path = require("node:path");
const Database = require("better-sqlite3");

class HttpError extends Error {
    constructor(status, message) {
        super(message);
        this.status = status;
    }
}

function parseInteger(value, name, minimum = 0) {
    const number = Number(value);

    if (!Number.isInteger(number) || number < minimum) {
        throw new HttpError(400, `Поле ${name} заполнено неверно`);
    }

    return number;
}

function parsePrice(value) {
    const price = Number(value);

    if (!Number.isFinite(price) || price < 0) {
        throw new HttpError(400, "Цена должна быть неотрицательным числом");
    }

    return Math.round(price * 100) / 100;
}

function validateTicket(ticket) {
    const type = String(ticket.type ?? "").toLowerCase();

    if (!['general', 'vip'].includes(type)) {
        throw new HttpError(400, "Тип билета должен быть general или vip");
    }

    return {
        event_id: parseInteger(ticket.event_id, "event_id", 1),
        type,
        price: parsePrice(ticket.price),
        remaining: parseInteger(ticket.remaining, "remaining")
    };
}

function createTicketStore(filename = path.join(__dirname, "..", "data", "tickets.db"), seed = true) {
    if (filename !== ":memory:") {
        fs.mkdirSync(path.dirname(filename), { recursive: true });
    }

    const db = new Database(filename);
    db.pragma("journal_mode = WAL");
    db.exec(`
        CREATE TABLE IF NOT EXISTS tickets (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            event_id INTEGER NOT NULL,
            type TEXT NOT NULL CHECK(type IN ('general', 'vip')),
            price REAL NOT NULL CHECK(price >= 0),
            remaining INTEGER NOT NULL
        )
    `);

    const listStatement = db.prepare("SELECT * FROM tickets ORDER BY event_id, type, id");
    const listByEventStatement = db.prepare("SELECT * FROM tickets WHERE event_id = ? ORDER BY type, id");
    const getStatement = db.prepare("SELECT * FROM tickets WHERE id = ?");
    const insertStatement = db.prepare(`
        INSERT INTO tickets (event_id, type, price, remaining)
        VALUES (@event_id, @type, @price, @remaining)
    `);
    const updateStatement = db.prepare(`
        UPDATE tickets
        SET event_id = @event_id, type = @type, price = @price, remaining = @remaining
        WHERE id = @id
    `);
    const deleteStatement = db.prepare("DELETE FROM tickets WHERE id = ?");
    const decrementStatement = db.prepare("UPDATE tickets SET remaining = remaining - ? WHERE id = ?");

    const seedDatabase = db.transaction(() => {
        const samples = [
            { event_id: 7, type: "general", price: 1200, remaining: 34 },
            { event_id: 7, type: "vip", price: 2800, remaining: 8 },
            { event_id: 12, type: "general", price: 900, remaining: 52 },
            { event_id: 12, type: "vip", price: 2100, remaining: 10 }
        ];

        for (const ticket of samples) {
            insertStatement.run(ticket);
        }
    });

    if (seed && db.prepare("SELECT COUNT(*) AS count FROM tickets").get().count === 0) {
        seedDatabase();
    }

    const purchaseTransaction = db.transaction((id, quantity) => {
        const ticket = getStatement.get(id);

        if (!ticket) {
            throw new HttpError(404, "Билет не найден");
        }

        decrementStatement.run(quantity, id);
        const updated = getStatement.get(id);

        if (updated.remaining < 0) {
            throw new HttpError(400, "Недостаточно билетов в остатке");
        }

        return updated;
    });

    return {
        list(eventId) {
            if (eventId === undefined) {
                return listStatement.all();
            }

            return listByEventStatement.all(parseInteger(eventId, "event_id", 1));
        },

        get(id) {
            const ticket = getStatement.get(parseInteger(id, "id", 1));

            if (!ticket) {
                throw new HttpError(404, "Билет не найден");
            }

            return ticket;
        },

        create(input) {
            const ticket = validateTicket(input);
            const result = insertStatement.run(ticket);
            return getStatement.get(result.lastInsertRowid);
        },

        update(id, input) {
            const current = this.get(id);
            const ticket = validateTicket({ ...current, ...input });
            updateStatement.run({ id: current.id, ...ticket });
            return getStatement.get(current.id);
        },

        remove(id) {
            const ticketId = parseInteger(id, "id", 1);
            const result = deleteStatement.run(ticketId);

            if (result.changes === 0) {
                throw new HttpError(404, "Билет не найден");
            }
        },

        purchase(id, quantity = 1) {
            const ticketId = parseInteger(id, "id", 1);
            const amount = parseInteger(quantity, "quantity", 1);
            return purchaseTransaction(ticketId, amount);
        },

        close() {
            db.close();
        }
    };
}

module.exports = { createTicketStore, HttpError };
