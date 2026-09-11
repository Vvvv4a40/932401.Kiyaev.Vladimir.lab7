const ticketsContainer = document.querySelector("#tickets");
const statusElement = document.querySelector("#status");
const filterForm = document.querySelector("#filter-form");
const ticketForm = document.querySelector("#ticket-form");

function escapeHtml(value) {
    return String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");
}

function showStatus(message, isError = false) {
    statusElement.textContent = message;
    statusElement.classList.toggle("error", isError);
}

async function request(url, options = {}) {
    const response = await fetch(url, {
        headers: { "Content-Type": "application/json" },
        ...options
    });

    if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(body.error || "Не удалось выполнить запрос");
    }

    return response.status === 204 ? null : response.json();
}

function renderTickets(tickets) {
    if (tickets.length === 0) {
        ticketsContainer.innerHTML = '<p class="empty">Для этого события билетов пока нет.</p>';
        return;
    }

    ticketsContainer.innerHTML = tickets.map((ticket) => `
        <article class="ticket-card">
            <div class="ticket-topline">
                <span>Событие ${escapeHtml(ticket.event_id)}</span>
                <span class="ticket-type">${escapeHtml(ticket.type)}</span>
            </div>
            <p class="price">${escapeHtml(ticket.price)} ₽</p>
            <p class="remaining">Осталось: <strong>${escapeHtml(ticket.remaining)}</strong></p>
            <div class="ticket-actions">
                <button type="button" data-action="buy" data-id="${ticket.id}" ${ticket.remaining === 0 ? "disabled" : ""}>Купить</button>
                <button type="button" class="secondary" data-action="delete" data-id="${ticket.id}">Удалить</button>
            </div>
        </article>
    `).join("");
}

async function loadTickets() {
    const eventId = document.querySelector("#event-filter").value.trim();
    const query = eventId ? `?event_id=${encodeURIComponent(eventId)}` : "";

    try {
        const tickets = await request(`/api/tickets${query}`);
        renderTickets(tickets);
        showStatus(`Найдено тарифов: ${tickets.length}`);
    } catch (error) {
        showStatus(error.message, true);
    }
}

filterForm.addEventListener("submit", (event) => {
    event.preventDefault();
    loadTickets();
});

ticketForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const data = Object.fromEntries(new FormData(ticketForm));

    try {
        await request("/api/tickets", {
            method: "POST",
            body: JSON.stringify(data)
        });
        showStatus("Тариф добавлен");
        await loadTickets();
    } catch (error) {
        showStatus(error.message, true);
    }
});

ticketsContainer.addEventListener("click", async (event) => {
    const button = event.target.closest("button[data-action]");

    if (!button) {
        return;
    }

    try {
        if (button.dataset.action === "buy") {
            await request(`/api/tickets/${button.dataset.id}/purchase`, {
                method: "POST",
                body: JSON.stringify({ quantity: 1 })
            });
            showStatus("Покупка прошла успешно");
        } else {
            await request(`/api/tickets/${button.dataset.id}`, { method: "DELETE" });
            showStatus("Тариф удалён");
        }

        await loadTickets();
    } catch (error) {
        showStatus(error.message, true);
    }
});

loadTickets();
