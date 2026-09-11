const test = require("node:test");
const assert = require("node:assert/strict");
const { createTicketStore } = require("../src/database");

test("создание, чтение, обновление и удаление билета", () => {
    const store = createTicketStore(":memory:", false);
    const created = store.create({ event_id: 7, type: "general", price: 1000, remaining: 5 });

    assert.equal(store.get(created.id).remaining, 5);
    assert.equal(store.list(7).length, 1);
    assert.equal(store.update(created.id, { price: 1250 }).price, 1250);

    store.remove(created.id);
    assert.equal(store.list().length, 0);
    store.close();
});

test("покупка уменьшает остаток внутри транзакции", () => {
    const store = createTicketStore(":memory:", false);
    const ticket = store.create({ event_id: 12, type: "vip", price: 2400, remaining: 2 });

    assert.equal(store.purchase(ticket.id, 1).remaining, 1);
    assert.throws(() => store.purchase(ticket.id, 2), /Недостаточно билетов/);
    assert.equal(store.get(ticket.id).remaining, 1);
    store.close();
});
