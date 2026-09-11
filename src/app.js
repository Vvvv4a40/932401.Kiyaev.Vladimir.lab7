const path = require("node:path");
const express = require("express");
const { createTicketStore } = require("./database");

function safeRoute(handler) {
    return (request, response, next) => {
        try {
            handler(request, response);
        } catch (error) {
            next(error);
        }
    };
}

function createApp(store = createTicketStore()) {
    const app = express();

    app.locals.store = store;
    app.use(express.json());
    app.use(express.static(path.join(__dirname, "..", "public")));

    app.get("/api/tickets", safeRoute((request, response) => {
        response.json(store.list(request.query.event_id));
    }));

    app.get("/api/tickets/:id", safeRoute((request, response) => {
        response.json(store.get(request.params.id));
    }));

    app.post("/api/tickets", safeRoute((request, response) => {
        response.status(201).json(store.create(request.body));
    }));

    app.patch("/api/tickets/:id", safeRoute((request, response) => {
        response.json(store.update(request.params.id, request.body));
    }));

    app.post("/api/tickets/:id/purchase", safeRoute((request, response) => {
        response.json(store.purchase(request.params.id, request.body.quantity));
    }));

    app.delete("/api/tickets/:id", safeRoute((request, response) => {
        store.remove(request.params.id);
        response.status(204).end();
    }));

    app.use("/api", (request, response) => {
        response.status(404).json({ error: "Маршрут не найден" });
    });

    app.use((error, request, response, next) => {
        const status = error.status || 500;
        const message = status === 500 ? "Внутренняя ошибка сервера" : error.message;
        response.status(status).json({ error: message });
    });

    return app;
}

module.exports = { createApp };
