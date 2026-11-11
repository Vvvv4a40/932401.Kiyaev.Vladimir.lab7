# Лабораторная работа №7

Вариант 12. Билеты на мероприятия. Можно добавить тариф, изменить цену и количество, купить билет или удалить тариф. При покупке остаток уменьшается. Если билетов не хватает, покупка не проходит.

Сервер на C# и ASP.NET Core. База SQLite создаётся в `data/tickets.db`. Нужен .NET SDK 10.

## Запуск

В папке лабораторной:

```sh
dotnet run
```

Открыть [localhost:3000](http://localhost:3000).

API: `/api/tickets`. Покупка: `POST /api/tickets/{id}/purchase`.

## Тесты

```sh
dotnet run --project tests/Lab7.Tests.csproj
```
