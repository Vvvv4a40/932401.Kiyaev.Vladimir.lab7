# Лабораторная работа №7

Express API для билетов на мероприятия, вариант 12. Данные хранятся в SQLite.

## Запуск

```bash
npm install
npm start
```

После запуска страница доступна по адресу `http://localhost:3000`.

## API

- `GET /api/tickets` — все тарифы;
- `GET /api/tickets?event_id=7` — фильтр по событию;
- `GET /api/tickets/:id` — один тариф;
- `POST /api/tickets` — создание;
- `PATCH /api/tickets/:id` — изменение;
- `POST /api/tickets/:id/purchase` — покупка и уменьшение остатка;
- `DELETE /api/tickets/:id` — удаление.

Таблица `tickets`: `id`, `event_id`, `type`, `price`, `remaining`. Покупка выполняется в транзакции, поэтому при недостаточном остатке изменение откатывается.

Тесты запускаются командой `npm test`.
