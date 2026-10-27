"use strict";
(() => {
    const $ = (id) => document.getElementById(id),
        page = document.body.dataset.page,
        csrf = document.querySelector('meta[name="csrf-token"]').content;
    const esc = (v) =>
        String(v ?? "").replace(
            /[&<>"']/g,
            (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c],
        );
    const hours = (n) => Math.floor(n / 60) + "h " + String(n % 60).padStart(2, "0") + "m";
    const clock = (n) =>
        String(Math.floor(n / 60)).padStart(2, "0") + ":" + String(n % 60).padStart(2, "0");
    const dateOf = (v) => new Date(v + "T12:00:00"),
        iso = (d) =>
            d.getFullYear() +
            "-" +
            String(d.getMonth() + 1).padStart(2, "0") +
            "-" +
            String(d.getDate()).padStart(2, "0");
    const dateLabel = (v) =>
        dateOf(v).toLocaleDateString("en-AU", { day: "numeric", month: "short", year: "numeric" });
    const noticeTimers = new WeakMap();
    function scheduleNoticeDismissal(el) {
        if (!el || el.id === "entry-timeline" || el.hasAttribute("data-persist")) return;
        clearTimeout(noticeTimers.get(el));
        if (el.hidden || !el.textContent.trim()) {
            noticeTimers.delete(el);
            return;
        }
        noticeTimers.set(
            el,
            setTimeout(() => {
                el.hidden = true;
            }, 3000),
        );
    }
    const error = (id, message) => {
        const e = $(id);
        e.textContent = message || "";
        e.hidden = !message;
        scheduleNoticeDismissal(e);
    };
    document.querySelectorAll(".notice").forEach(scheduleNoticeDismissal);
    let timer;
    function toast(message) {
        $("toast").textContent = message;
        $("toast").hidden = false;
        clearTimeout(timer);
        timer = setTimeout(() => ($("toast").hidden = true), 4000);
    }
    async function api(url, method = "GET", body) {
        const r = await fetch(url, {
            method,
            credentials: "same-origin",
            headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf },
            body: body === undefined ? undefined : JSON.stringify(body),
        });
        const data = r.status === 204 ? null : await r.json().catch(() => null);
        if (!r.ok)
            throw Error(
                data?.message ||
                    (data?.errors ? Object.values(data.errors).flat().join(" ") : data?.title) ||
                    (r.status === 401
                        ? "Windows authentication is required. Open this site from your Windows/RDS session."
                        : r.status === 403
                          ? "Administrator access is locked. Use Administration in the navigation to unlock."
                          : "Unable to complete the request."),
            );
        return data;
    }
    document.querySelector(".mobile-toggle")?.addEventListener("click", (e) => {
        e.currentTarget.setAttribute(
            "aria-expanded",
            String($("navigation").classList.toggle("open")),
        );
    });
    document
        .querySelectorAll("[data-close]")
        .forEach((b) => b.addEventListener("click", () => b.closest("dialog").close()));
    if (page === "employees" || page === "task-types") {
        management();
        return;
    }
    if (page !== "timesheet" && page !== "admin") return;
    window.WLTimesheet({ $, page, api, esc, hours, clock, dateOf, iso, dateLabel, error, toast });
    function management() {
        const employees = page === "employees",
            endpoint = employees ? "employees" : "task-types";
        let records = [],
            sequence = 0,
            searchTimer,
            pageNumber = 1,
            pageSize = 20,
            totalPages = 1,
            loading = false;
        function busy(value) {
            loading = value;
            $("records").setAttribute("aria-busy", String(value));
            $("page-previous").disabled = value || pageNumber <= 1;
            $("page-next").disabled = value || pageNumber >= totalPages;
            $("page-number").disabled = value;
        }
        async function reload() {
            const seq = ++sequence;
            busy(true);
            error("page-error", "");
            try {
                const data = await api(
                    "/api/" +
                        endpoint +
                        "/page?" +
                        new URLSearchParams({
                            search: $("management-search").value,
                            page: pageNumber,
                            pageSize,
                        }),
                );
                if (seq !== sequence) return;
                records = data.rows;
                pageNumber = data.page;
                totalPages = data.pages;
                $("record-count").textContent =
                    data.total + " " + (employees ? "employees" : "task types");
                const first = data.total ? (pageNumber - 1) * pageSize + 1 : 0,
                    last = Math.min(pageNumber * pageSize, data.total);
                $("page-range").textContent = "Showing " + first + "–" + last + " of " + data.total;
                $("page-number").value = pageNumber;
                $("page-number").max = totalPages;
                $("page-count").textContent = "of " + totalPages;
                $("records").innerHTML =
                    records
                        .map(
                            (r) =>
                                '<article class="record-row"><div class="record-info"><strong>' +
                                esc(employees ? r.displayName : r.name) +
                                "</strong><p>" +
                                esc(
                                    employees
                                        ? r.windowsAccount || "Unlinked legacy profile"
                                        : r.explanatoryNotes || "No explanatory notes",
                                ) +
                                "</p>" +
                                (!employees
                                    ? "<small>" +
                                      (r.isBreak ? "Break" : "Productive work") +
                                      " · " +
                                      [
                                          "Optional description",
                                          "Description required",
                                          "Files closed required",
                                      ][r.descriptionRule] +
                                      "</small>"
                                    : "") +
                                '</div><span class="badge ' +
                                (r.isActive ? "" : "inactive") +
                                '">' +
                                (r.isActive ? "Active" : "Inactive") +
                                '</span><button class="button small" data-record="' +
                                r.id +
                                '">Edit</button></article>',
                        )
                        .join("") || '<div class="empty-state">No records match this search.</div>';
            } catch (e) {
                if (seq === sequence) error("page-error", e.message);
            } finally {
                if (seq === sequence) busy(false);
            }
        }
        function open(r) {
            $("management-form").reset();
            error("management-error", "");
            $("management-title").textContent =
                (r ? "Edit " : "Add ") + (employees ? "employee" : "task type");
            $("record-id").value = r?.id || "";
            $("record-version").value = r?.version || "";
            $("record-name").value = (employees ? r?.displayName : r?.name) || "";
            $("record-active").checked = r?.isActive ?? true;
            if (employees) {
                $("record-email").value = r?.email || "";
                $("record-account").textContent = r.windowsAccount || "Unlinked legacy account";
            } else {
                $("record-notes").value = r?.explanatoryNotes || "";
                $("record-break").checked = r?.isBreak ?? false;
                $("record-rule").value = r?.descriptionRule ?? 0;
            }
            $("management-dialog").showModal();
            $("record-name").focus();
        }
        $("add-record")?.addEventListener("click", () => open(null));
        $("records").addEventListener("click", (e) => {
            const b = e.target.closest("[data-record]");
            if (b && !loading) open(records.find((r) => r.id === b.dataset.record));
        });
        $("management-search").addEventListener("input", () => {
            clearTimeout(searchTimer);
            sequence++;
            pageNumber = 1;
            busy(true);
            searchTimer = setTimeout(reload, 200);
        });
        $("page-size").addEventListener("change", () => {
            clearTimeout(searchTimer);
            pageSize = Number($("page-size").value);
            pageNumber = 1;
            reload();
        });
        $("page-previous").addEventListener("click", () => {
            if (!loading && pageNumber > 1) {
                pageNumber--;
                reload();
            }
        });
        $("page-next").addEventListener("click", () => {
            if (!loading && pageNumber < totalPages) {
                pageNumber++;
                reload();
            }
        });
        $("page-number").addEventListener("change", () => {
            if (!loading && $("page-number").reportValidity()) {
                pageNumber = Number($("page-number").value);
                reload();
            }
        });
        $("management-form").addEventListener("submit", async (e) => {
            e.preventDefault();
            e.submitter.disabled = true;
            error("management-error", "");
            try {
                const id = $("record-id").value,
                    body = {
                        isActive: $("record-active").checked,
                        version: $("record-version").value || null,
                    };
                if (employees)
                    Object.assign(body, {
                        displayName: $("record-name").value,
                        email: $("record-email").value || null,
                    });
                else
                    Object.assign(body, {
                        name: $("record-name").value,
                        explanatoryNotes: $("record-notes").value,
                        isBreak: $("record-break").checked,
                        descriptionRule: Number($("record-rule").value),
                    });
                await api("/api/" + endpoint + (id ? "/" + id : ""), id ? "PUT" : "POST", body);
                $("management-dialog").close();
                toast("Changes saved.");
                await reload();
            } catch (ex) {
                error("management-error", ex.message);
            } finally {
                e.submitter.disabled = false;
            }
        });
        reload();
    }
})();
