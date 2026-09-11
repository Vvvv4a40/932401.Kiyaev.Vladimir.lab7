const { createApp } = require("./src/app");

const port = Number(process.env.PORT) || 3000;
const app = createApp();
const server = app.listen(port, () => {
    console.log(`Сервер запущен: http://localhost:${port}`);
});

function shutdown() {
    server.close(() => {
        app.locals.store.close();
        process.exit(0);
    });
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
