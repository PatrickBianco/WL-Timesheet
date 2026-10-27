"use strict";
// Shared API, calculations and task picker; intentionally different entry surfaces for list/calendar.
window.WLTimesheet = function ({
    $,
    page,
    api,
    esc,
    hours,
    clock,
    dateOf,
    iso,
    dateLabel,
    error,
    toast,
}) {
    const admin = page === "admin",
        today = $("date-from").value,
        ownId = document.body.dataset.employee;
    const icon = (name) => '<span class="icon icon-' + name + '" aria-hidden="true"></span>';
    let mode = "list",
        selectedDate = today,
        monthAnchor = today,
        types = [],
        rows = [],
        day = null,
        summaries = [],
        pageNumber = 1,
        pageSize = 20,
        overviewPage = 1;
    let sequence = 0,
        loading = false,
        result = null,
        newEditor = null,
        editEditor = null,
        pendingDelete = null,
        drawer = null;
    let dailySaving = false,
        dailyDirty = false;
    function selectionParams(month = false) {
        const p = new URLSearchParams({
            from: selectedDate,
            to: admin && mode === "list" ? $("date-to").value : selectedDate,
        });
        const employee = admin ? $("employee-filter").value : ownId;
        if (employee) p.set("employeeId", employee);
        if ($("type-filter").value) p.set("taskTypeId", $("type-filter").value);
        if (month) {
            const d = dateOf(monthAnchor);
            p.set("from", iso(new Date(d.getFullYear(), d.getMonth(), 1, 12)));
            p.set("to", iso(new Date(d.getFullYear(), d.getMonth() + 1, 0, 12)));
        }
        return p;
    }
    async function fetchDay(date) {
        if (admin && !$("employee-filter").value) return null;
        const p = new URLSearchParams({ date });
        if (admin) p.set("employeeId", $("employee-filter").value);
        return api("/api/timesheets/day?" + p);
    }
    function setBusy(value) {
        loading = value;
        $("loading").hidden = !value;
        $("list-view").setAttribute("aria-busy", String(value));
        $("calendar-view").setAttribute("aria-busy", String(value));
        $("export").disabled = value;
        $("export-csv").disabled = value;
        if (!admin) $("calendar-add").disabled = value || !day?.canEdit;
    }
    function stats(value) {
        $("total-time").textContent = hours(value.minutes);
        $("total-breaks").textContent = hours(value.breakMinutes);
        $("total-units").textContent = value.units;
        $("total-tasks").textContent = value.total;
        $("elapsed-caption").textContent = "Elapsed time " + hours(value.elapsedMinutes);
        $("total-caption").textContent =
            (mode === "calendar" ? "Selected month" : "Selected dates") + " · breaks excluded";
        $("result-count").textContent =
            value.total +
            " tasks · " +
            hours(value.minutes) +
            " productive · " +
            hours(value.breakMinutes) +
            " breaks";
    }
    async function load({ month = true } = {}) {
        const ticket = ++sequence;
        setBusy(true);
        error("page-error", "");
        const p = selectionParams();
        p.set("page", mode === "list" ? pageNumber : overviewPage);
        p.set("pageSize", mode === "list" ? pageSize : 20);
        const cp = selectionParams(true);
        cp.set("month", cp.get("from"));
        try {
            const [data, detail, calendar] = await Promise.all([
                api("/api/timesheets?" + p),
                fetchDay(selectedDate),
                mode === "calendar" && month
                    ? api("/api/timesheets/calendar?" + cp)
                    : Promise.resolve(null),
            ]);
            if (ticket !== sequence) return;
            result = data;
            rows = data.rows;
            day = detail;
            if (mode === "list") {
                pageNumber = data.page;
                renderRows();
                renderPagination();
                stats(data);
                $("date-title").textContent = dateLabel(selectedDate);
                if (!admin) {
                    if (!newEditor) newEditor = createInline(null, $("inline-new-host"));
                    if (newEditor.date !== selectedDate) newEditor.reset();
                    newEditor.date = selectedDate;
                    newEditor.refresh();
                }
            } else {
                overviewPage = data.page;
                if (calendar) {
                    summaries = calendar;
                    renderCalendar();
                }
                renderOverview();
                markSelectedDay();
                stats(
                    summaries.reduce(
                        (a, d) => ({
                            minutes: a.minutes + d.minutes,
                            breakMinutes: a.breakMinutes + d.breakMinutes,
                            elapsedMinutes: a.elapsedMinutes + d.elapsedMinutes,
                            units: a.units + d.units,
                            total: a.total + d.count,
                        }),
                        { minutes: 0, breakMinutes: 0, elapsedMinutes: 0, units: 0, total: 0 },
                    ),
                );
                $("date-title").textContent = dateOf(monthAnchor).toLocaleDateString("en-AU", {
                    month: "long",
                    year: "numeric",
                });
            }
            renderDayDetails();
            if (admin)
                $("period-totals").innerHTML = data.weekly
                    .map(
                        (x) =>
                            '<div class="period-item">Week of ' +
                            esc(dateLabel(x.week)) +
                            "<strong>" +
                            hours(x.minutes) +
                            "</strong></div>",
                    )
                    .join("");
            return true;
        } catch (ex) {
            if (ticket === sequence) {
                error("page-error", ex.message);
                day = null;
                newEditor?.refresh();
            }
            return false;
        } finally {
            if (ticket === sequence) setBusy(false);
        }
    }
    function renderDayDetails() {
        const minutes = day?.minutesPerUnit;
        const caption = minutes
            ? "1 unit = " + minutes + (minutes === 1 ? " minute" : " minutes")
            : "Unit settings may differ by daily timesheet";
        $("unit-caption").textContent = caption;
        if ($("entry-unit-hint")) $("entry-unit-hint").textContent = "Whole numbers · " + caption;
        if (admin) {
            $("reopen-section").hidden = !day;
            $("reopen-day").disabled = !day || selectedDate > today;
            $("reopen-status").textContent = !day
                ? ""
                : !day.id
                  ? day.canEdit
                      ? "No tasks yet. The employee can start this day."
                      : "No timesheet yet. Reopen this date so the employee can start it."
                  : day.canEdit
                    ? "The employee can edit this day."
                    : "This day is read-only for the employee.";
            return;
        }
        const home = $(mode === "list" ? "day-form-home" : "calendar-day-details");
        if ($("day-form").parentElement !== home) home.append($("day-form"));
        if (!dailySaving && !dailyDirty) {
            $("day-name").value = day.displayName;
            $("clock-in").value = day.clockInTime || "";
        }
        $("day-form")
            .querySelectorAll("input,button")
            .forEach((x) => (x.disabled = !day.canEdit));
        $("day-status").textContent = dailySaving
            ? "Saving daily details…"
            : !day.canEdit
              ? "Read-only. Ask an administrator to reopen a past day."
              : !day.id
                ? "Enter your clock-in time. Daily details save when you leave the field."
                : "Daily details saved automatically.";
        roundPreview();
    }
    function roundPreview() {
        const value = $("clock-in").value.split(":").map(Number),
            n = value[0] * 60 + value[1];
        $("rounded-start").textContent = Number.isFinite(n) ? "First start: " + clock(n) : "";
    }
    function rowHtml(r) {
        return (
            '<article class="entry-row list-columns" data-row="' +
            r.id +
            '"><div class="entry-time"><div>' +
            esc(r.startTime) +
            " – " +
            esc(r.endTime) +
            '</div><span class="duration-chip">' +
            hours(r.durationMinutes) +
            '</span></div><div class="entry-units" title="1 unit = ' + esc(r.minutesPerUnit) + ' minute(s)">' +
            r.units +
            '</div><div class="entry-detail">' +
            (admin
                ? '<div class="entry-meta">' +
                  esc(r.employeeName) +
                  " · " +
                  esc(dateLabel(r.workDate)) +
                  " · " + esc(r.minutesPerUnit) + " min/unit" +
                  "</div>"
                : "") +
            "<strong>" +
            esc(r.taskTypeName) +
            "</strong>" +
            (r.isBreak ? ' <span class="badge">Break</span>' : "") +
            '</div><p class="entry-description">' +
            esc(r.description) +
            '</p><div class="row-actions">' +
            (!admin && r.canEdit
                ? '<button type="button" class="text-button" data-edit="' +
                  r.id +
                  '" aria-label="Edit ' +
                  esc(r.taskTypeName) +
                  '">' +
                  icon("pencil") +
                  '</button><button type="button" class="text-button delete" data-delete="' +
                  r.id +
                  '" aria-label="Delete ' +
                  esc(r.taskTypeName) +
                  '">' +
                  icon("trash-2") +
                  "</button>"
                : '<span class="badge">' + (r.canEdit ? "Recorded" : "Read-only") + "</span>") +
            "</div></article>"
        );
    }
    function renderRows() {
        editEditor = null;
        $("entries").innerHTML = rows.map(rowHtml).join("");
    }
    function renderPagination() {
        const first = result.total ? (result.page - 1) * result.pageSize + 1 : 0,
            last = Math.min(result.page * result.pageSize, result.total);
        $("page-range").textContent = "Showing " + first + "–" + last + " of " + result.total;
        $("page-previous").disabled = result.page <= 1;
        $("page-next").disabled = result.page >= result.pages;
        $("page-number").value = result.page;
        $("page-number").max = result.pages;
        $("page-count").textContent = "of " + result.pages;
    }
    function renderCalendar() {
        const month = dateOf(monthAnchor);
        month.setDate(1);
        const first = new Date(month);
        first.setDate(first.getDate() - ((first.getDay() + 6) % 7));
        const totals = new Map(summaries.map((x) => [x.date, x]));
        $("calendar").innerHTML = Array.from({ length: 42 }, (_, i) => {
            const d = new Date(first);
            d.setDate(d.getDate() + i);
            const key = iso(d),
                item = totals.get(key),
                outside = d.getMonth() !== month.getMonth();
            const previews = item?.previews || [],
                more = Math.max(0, (item?.count || 0) - previews.length);
            return (
                '<div class="calendar-cell ' +
                (outside ? "other-month " : "") +
                (key === today ? "is-today " : "") +
                '" data-cell="' +
                key +
                '"><button type="button" class="calendar-day" data-day="' +
                key +
                '" aria-label="' +
                esc(dateLabel(key)) +
                ", " +
                (item?.count || 0) +
                ' tasks"><span class="day-number">' +
                d.getDate() +
                "</span>" +
                (item?.count
                    ? '<span class="day-total">' +
                      hours(item.minutes) +
                      '</span><span class="day-count">' +
                      item.count +
                      " tasks</span>"
                    : "") +
                previews
                    .map((name) => '<span class="calendar-task-label">' + esc(name) + "</span>")
                    .join("") +
                (more ? '<span class="day-more">+' + more + " more</span>" : "") +
                "</button>" +
                (!admin && !outside && item?.canEdit
                    ? '<button type="button" class="calendar-plus" data-add-day="' +
                      key +
                      '" aria-label="Add task for ' +
                      esc(dateLabel(key)) +
                      '">' +
                      icon("plus") +
                      "</button>"
                    : "") +
                "</div>"
            );
        }).join("");
        markSelectedDay();
    }
    function markSelectedDay() {
        $("calendar")
            .querySelectorAll("[data-cell]")
            .forEach((c) => c.classList.toggle("is-selected", c.dataset.cell === selectedDate));
        $("calendar")
            .querySelectorAll("[data-day]")
            .forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.day === selectedDate)));
    }
    function renderOverview() {
        $("overview-title").textContent = dateLabel(selectedDate);
        $("overview-total").textContent =
            result.total + " tasks · " + hours(result.minutes) + " productive";
        $("overview-entries").innerHTML = rows.length
            ? rows
                  .map(
                      (r) =>
                          '<article class="overview-task"><div class="entry-meta">' +
                          esc(r.startTime) +
                          " – " +
                          esc(r.endTime) +
                          " · " +
                          r.units +
                          " units</div><strong>" +
                          esc(r.taskTypeName) +
                          "</strong><p>" +
                          esc(r.description || "No description") +
                          "</p>" +
                          (r.isBreak ? '<span class="badge">Break</span>' : "") +
                          (admin
                              ? "<small>" + esc(r.employeeName) + "</small>"
                              : r.canEdit
                                ? '<div class="row-actions"><button type="button" class="text-button" data-edit="' +
                                  r.id +
                                  '">View / edit</button><button type="button" class="text-button delete" data-delete="' +
                                  r.id +
                                  '">Delete</button></div>'
                                : '<span class="badge inactive">Read-only</span>') +
                          "</article>",
                  )
                  .join("")
            : '<div class="overview-empty"><strong>No tasks recorded</strong><p>' +
              (admin
                  ? "Select another day to review time."
                  : "Use Add task to record time for this day.") +
              "</p></div>";
        $("overview-previous").disabled = result.page <= 1;
        $("overview-next").disabled = result.page >= result.pages;
        $("overview-range").textContent = "Page " + result.page + " of " + result.pages;
        $("overview-previous").parentElement.hidden = result.pages <= 1;
    }
    async function selectDay(date, add = false) {
        if (isSaving()) return;
        selectedDate = date;
        $("date-from").value = date;
        overviewPage = 1;
        markSelectedDay();
        if ((await load({ month: false })) && add && selectedDate === date) openDrawer(null);
    }
    function applyMode() {
        $("list-view").hidden = mode !== "list";
        $("calendar-view").hidden = mode !== "calendar";
        $("day-form-home")?.toggleAttribute("hidden", mode !== "list");
        $("list-section-heading").hidden = mode !== "list";
        $("list-mode").setAttribute("aria-pressed", String(mode === "list"));
        $("calendar-mode").setAttribute("aria-pressed", String(mode === "calendar"));
        $(mode === "list" ? "list-section-heading" : "calendar-export-home").append(
            $("export-actions"),
        );
        if (admin) $("date-to").disabled = mode === "calendar";
    }
    function isSaving() {
        return !!(dailySaving || dailyDirty || newEditor?.busy || editEditor?.busy || drawer?.busy);
    }
    async function switchMode(next) {
        if (isSaving() || next === mode) return;
        mode = next;
        editEditor = null;
        pageNumber = 1;
        overviewPage = 1;
        monthAnchor = selectedDate;
        applyMode();
        await load();
    }
    $("list-mode").addEventListener("click", () => switchMode("list"));
    $("calendar-mode").addEventListener("click", () => switchMode("calendar"));
    $("calendar").addEventListener("click", (e) => {
        const add = e.target.closest("[data-add-day]"),
            button = e.target.closest("[data-day]");
        if (add) selectDay(add.dataset.addDay, true);
        else if (button) selectDay(button.dataset.day);
    });
    $("filters").addEventListener("submit", (e) => {
        e.preventDefault();
        if (isSaving()) return;
        selectedDate = $("date-from").value;
        monthAnchor = selectedDate;
        pageNumber = overviewPage = 1;
        load();
    });
    function navigate(delta) {
        if (isSaving()) return;
        const d = dateOf(mode === "calendar" ? monthAnchor : selectedDate);
        if (mode === "calendar") {
            d.setDate(1);
            d.setMonth(d.getMonth() + delta);
        } else d.setDate(d.getDate() + delta);
        selectedDate = iso(d);
        monthAnchor = selectedDate;
        $("date-from").value = selectedDate;
        if (admin) $("date-to").value = selectedDate;
        pageNumber = overviewPage = 1;
        load();
    }
    $("previous").addEventListener("click", () => navigate(-1));
    $("next").addEventListener("click", () => navigate(1));
    $("today").addEventListener("click", () => {
        if (isSaving()) return;
        selectedDate = monthAnchor = today;
        $("date-from").value = today;
        if (admin) $("date-to").value = today;
        pageNumber = overviewPage = 1;
        load();
    });
    $("page-size").addEventListener("change", () => {
        if (isSaving()) return;
        pageSize = Number($("page-size").value);
        pageNumber = 1;
        load();
    });
    $("page-previous").addEventListener("click", () => {
        if (!loading && !isSaving()) {
            pageNumber--;
            load();
        }
    });
    $("page-next").addEventListener("click", () => {
        if (!loading && !isSaving()) {
            pageNumber++;
            load();
        }
    });
    $("page-number").addEventListener("change", () => {
        if (isSaving() || !$("page-number").reportValidity()) return;
        pageNumber = Number($("page-number").value);
        load();
    });
    $("overview-previous").addEventListener("click", () => {
        if (!loading && !isSaving()) {
            overviewPage--;
            load({ month: false });
        }
    });
    $("overview-next").addEventListener("click", () => {
        if (!loading && !isSaving()) {
            overviewPage++;
            load({ month: false });
        }
    });
    async function download(format, button) {
        button.disabled = true;
        try {
            const p = selectionParams(mode === "calendar");
            p.set("format", format);
            const r = await fetch(
                "/api/timesheets/" + (admin ? "admin-export" : "export") + "?" + p,
            );
            if (!r.ok) {
                const data = await r.json().catch(() => null);
                throw Error(data?.message || "Export failed.");
            }
            const url = URL.createObjectURL(await r.blob()),
                a = document.createElement("a");
            a.href = url;
            a.download = "WL-timesheets-" + p.get("from") + "-" + p.get("to") + "." + format;
            a.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (ex) {
            error("page-error", ex.message);
        } finally {
            button.disabled = false;
        }
    }
    $("export").addEventListener("click", (e) => download("xlsx", e.currentTarget));
    $("export-csv").addEventListener("click", (e) => download("csv", e.currentTarget));

    // One searchable keyboard control for the inline row, inline edit and existing calendar drawer.
    function taskPicker(input, list, changed, commit = null) {
        const optionLabel = (t) =>
            t.name + (t.explanatoryNotes?.trim() ? " (" + t.explanatoryNotes.trim() + ")" : "");
        let selected = null,
            options = [],
            highlight = -1;
        input.setAttribute("aria-controls", list.id);
        input.setAttribute("role", "combobox");
        input.setAttribute("aria-autocomplete", "list");
        input.setAttribute("aria-expanded", "false");
        list.setAttribute("role", "listbox");
        const close = () => {
            list.hidden = true;
            input.setAttribute("aria-expanded", "false");
            input.removeAttribute("aria-activedescendant");
        };
        function search() {
            options = types.filter(
                (t) =>
                    t.isActive &&
                    t.name.toLocaleLowerCase().includes(input.value.toLocaleLowerCase()),
            );
            highlight = -1;
            list.innerHTML =
                options
                    .map(
                        (t, i) =>
                            '<div class="combo-option" id="' +
                            list.id +
                            "-" +
                            i +
                            '" role="option" aria-selected="false" data-option="' +
                            i +
                            '">' +
                            esc(optionLabel(t)) +
                            "</div>",
                    )
                    .join("") || '<div class="combo-option">No matching active task types</div>';
            list.hidden = false;
            input.setAttribute("aria-expanded", "true");
        }
        function choose(i) {
            const t = options[i];
            if (!t) return;
            selected = t;
            input.value = t.name;
            input.setCustomValidity("");
            changed(t);
            close();
        }
        input.addEventListener("input", () => {
            selected = null;
            input.setCustomValidity("");
            changed(null);
            search();
        });
        input.addEventListener("focus", search);
        input.addEventListener("blur", close);
        input.addEventListener("keydown", (e) => {
            if (e.key === "Tab") {
                if (!e.shiftKey && !selected && !list.hidden) {
                    const exact = options.findIndex(
                        (t) =>
                            t.name.toLocaleLowerCase() === input.value.trim().toLocaleLowerCase(),
                    );
                    if (highlight >= 0) choose(highlight);
                    else if (exact >= 0) choose(exact);
                    else if (input.value.trim() && options.length === 1) choose(0);
                }
                close();
                return;
            }
            if (e.key === "Escape" && !list.hidden) {
                e.preventDefault();
                e.stopPropagation();
                close();
                return;
            }
            if (e.key === "Enter" && !list.hidden) {
                e.preventDefault();
                e.stopPropagation();
                const wasSelected = !!selected;
                choose(highlight < 0 ? 0 : highlight);
                if (wasSelected && selected) commit?.();
                return;
            }
            if (!["ArrowDown", "ArrowUp"].includes(e.key)) return;
            e.preventDefault();
            if (list.hidden) search();
            if (!options.length) return;
            highlight =
                (highlight + (e.key === "ArrowDown" ? 1 : -1) + options.length) % options.length;
            list.querySelectorAll("[role=option]").forEach((node, i) =>
                node.setAttribute("aria-selected", String(i === highlight)),
            );
            const active = $(list.id + "-" + highlight);
            input.setAttribute("aria-activedescendant", active.id);
            active.scrollIntoView({ block: "nearest" });
        });
        list.addEventListener("mousedown", (e) => e.preventDefault());
        list.addEventListener("click", (e) => {
            const option = e.target.closest("[data-option]");
            if (option) choose(Number(option.dataset.option));
        });
        return {
            get value() {
                return selected;
            },
            set(t) {
                selected = t;
                input.value = t?.name || "";
                input.setCustomValidity("");
                changed(t);
                close();
            },
            close,
        };
    }
    // Display-only preview shared by both entry surfaces; all stored times/validation stay server-side.
    function wholeNumberInput(input) {
        input.inputMode = "numeric";
        input.addEventListener("keydown", (e) => {
            if (
                !e.ctrlKey &&
                !e.metaKey &&
                !e.altKey &&
                e.key.length === 1 &&
                !/^[0-9]$/.test(e.key)
            )
                e.preventDefault();
        });
        input.addEventListener("beforeinput", (e) => {
            if (e.data && !/^[0-9]+$/.test(e.data)) e.preventDefault();
        });
        input.addEventListener("paste", (e) => {
            if (!/^[0-9]+$/.test(e.clipboardData.getData("text"))) e.preventDefault();
        });
    }
    function preview(input, start, target, duration, minutesPerUnit = day?.minutesPerUnit ?? 1) {
        const maxUnits = Math.floor(1440 / minutesPerUnit);
        input.max = maxUnits;
        input.title = "1 unit = " + minutesPerUnit + " minute(s)";
        const n = Number(input.value),
            valid = input.value !== "" && Number.isInteger(n) && n >= 0 && n <= maxUnits;
        input.setCustomValidity(
            valid && start + n * minutesPerUnit > 1440 ? "This task would finish after midnight." : "",
        );
        target.textContent = clock(start) + (valid ? " – " + clock(start + n * minutesPerUnit) : "");
        if (duration) duration.textContent = valid ? hours(n * minutesPerUnit) : "Next task";
    }
    function typeFor(row) {
        return (
            types.find((t) => t.id === row.taskTypeId) || {
                id: row.taskTypeId,
                name: row.taskTypeName,
                explanatoryNotes: row.explanatoryNotes,
                isBreak: row.isBreak,
                descriptionRule: row.filesClosed != null ? 2 : 0,
                isActive: false,
            }
        );
    }
    function createInline(row, host) {
        const prefix = row ? "inline-edit" : "inline-new",
            form = document.createElement("form");
        form.id = prefix;
        form.className = "inline-entry list-columns";
        form.noValidate = true;
        form.innerHTML =
            '<div class="entry-time"><output data-time></output><span data-duration class="duration-chip">Next task</span></div><div><label class="mobile-field-label" for="' +
            prefix +
            '-units">Units</label><input id="' +
            prefix +
            '-units" type="number" required min="0" max="1440" step="1" placeholder="Units"/></div><div class="inline-task"><label class="mobile-field-label" for="' +
            prefix +
            '-type">Task type</label><div class="combobox"><input id="' +
            prefix +
            '-type" placeholder="Search task type…" required autocomplete="off"/><div id="' +
            prefix +
            '-options" hidden></div></div></div><div><label class="mobile-field-label" for="' +
            prefix +
            '-description">Description</label><input id="' +
            prefix +
            '-description" maxlength="4000" placeholder="Description (optional)"/><label id="' +
            prefix +
            '-files-label" hidden>Files closed<input id="' +
            prefix +
            '-files" type="number" min="0" max="1000000" step="1"/></label></div><div class="row-actions"><button type="submit" class="text-button" aria-label="Save row" title="Save row (Enter)">' +
            icon("check") +
            "</button>" +
            (row
                ? '<button type="button" data-cancel class="text-button" aria-label="Cancel editing">' +
                  icon("x") +
                  "</button>"
                : "") +
            '</div><p class="inline-validation" role="alert" hidden></p>';
        if (row) host.classList.add("editing-row");
        host.replaceChildren(form);
        const input = $(prefix + "-type"),
            description = $(prefix + "-description"),
            units = $(prefix + "-units"),
            files = $(prefix + "-files"),
            filesLabel = $(prefix + "-files-label"),
            notice = form.querySelector(".inline-validation");
        const editor = {
            date: selectedDate,
            busy: false,
            submissionId: crypto.randomUUID(),
            reset() {
                form.reset();
                picker.set(null);
                this.submissionId = crypto.randomUUID();
                notice.hidden = true;
            },
            refresh() {
                const enabled =
                    !!day?.canEdit && !!day?.id && !this.busy && !dailySaving && !dailyDirty;
                form.querySelectorAll("input,button").forEach((x) => (x.disabled = !enabled));
                update();
                $("inline-help").textContent = !day?.canEdit
                    ? "This day is read-only."
                    : !day.id
                      ? "Enter daily details above; they save automatically when you leave the field."
                      : "Units → Tab → Task type → Tab → Description → Enter or Tab to save.";
            },
            focus() {
                units.focus({ preventScroll: true });
            },
        };
        function rule(t) {
            const r = t?.descriptionRule || 0;
            filesLabel.hidden = r !== 2;
            files.required = r === 2;
            description.required = r === 1;
            description.placeholder = r === 1 ? "Description (required)" : "Description (optional)";
        }
        const picker = taskPicker(input, $(prefix + "-options"), rule, () => {
            if (
                units.value !== "" &&
                [...form.querySelectorAll("input")].every((x) => x.checkValidity())
            )
                form.requestSubmit(form.querySelector('[type="submit"]'));
        });
        wholeNumberInput(units);
        function update() {
            preview(
                units,
                row?.startMinute ?? day?.nextStartMinute ?? 0,
                form.querySelector("[data-time]"),
                form.querySelector("[data-duration]"),
                row?.minutesPerUnit ?? day?.minutesPerUnit ?? 1,
            );
        }
        units.addEventListener("input", update);
        if (row) {
            picker.set(typeFor(row));
            description.value = row.description;
            units.value = row.units;
            files.value = row.filesClosed ?? "";
        }
        editor.refresh();
        form.querySelector("[data-cancel]")?.addEventListener("click", () => {
            if (!editor.busy) {
                renderRows();
                newEditor.focus();
            }
        });
        form.addEventListener("keydown", (e) => {
            if (e.isComposing || e.defaultPrevented) return;
            const enter = e.key === "Enter" && e.target.tagName !== "BUTTON",
                tab =
                    e.key === "Tab" &&
                    !e.shiftKey &&
                    ((e.target === description && filesLabel.hidden) ||
                        (e.target === files && !filesLabel.hidden)) &&
                    (input.value || description.value || units.value);
            if (enter || tab) {
                e.preventDefault();
                form.requestSubmit(form.querySelector('[type="submit"]'));
            }
        });
        form.addEventListener("submit", async (e) => {
            e.preventDefault();
            if (editor.busy || loading || dailySaving || dailyDirty || !day?.canEdit || !day.id)
                return;
            update();
            input.setCustomValidity(picker.value ? "" : "Select a task type from the results.");
            if (!/^[0-9]+$/.test(units.value))
                units.setCustomValidity("Enter whole units within the displayed limit.");
            description.setCustomValidity(
                description.required && !description.value.trim()
                    ? "Enter a description for this task."
                    : "",
            );
            const invalid = [...form.querySelectorAll("input")].find(
                (x) => !x.disabled && !x.checkValidity(),
            );
            if (invalid) {
                notice.textContent = invalid.validationMessage;
                notice.hidden = false;
                invalid.focus({ preventScroll: true });
                return;
            }
            const body = {
                workDate: editor.date,
                taskTypeId: picker.value.id,
                description: description.value,
                units: Number(units.value),
                filesClosed: filesLabel.hidden ? null : Number(files.value),
                submissionId: editor.submissionId,
                version: row?.version ?? null,
                dayVersion: day.version,
            };
            editor.busy = true;
            form.setAttribute("aria-busy", "true");
            form.querySelectorAll("input,button").forEach((x) => (x.disabled = true));
            notice.hidden = true;
            try {
                await api(
                    "/api/timesheets" + (row ? "/" + row.id : ""),
                    row ? "PUT" : "POST",
                    body,
                );
                if (!row) editor.reset();
                await load();
                newEditor?.focus();
            } catch (ex) {
                notice.textContent = ex.message;
                notice.hidden = false;
            } finally {
                editor.busy = false;
                form.setAttribute("aria-busy", "false");
                editor.refresh();
                if (!notice.hidden) input.focus({ preventScroll: true });
                else if (!row) editor.focus();
            }
        });
        return editor;
    }
    function entryAction(e) {
        if (loading || isSaving()) return;
        const edit = e.target.closest("[data-edit]"),
            del = e.target.closest("[data-delete]");
        if (edit) {
            const row = rows.find((x) => x.id === edit.dataset.edit);
            if (mode === "calendar") openDrawer(row);
            else {
                renderRows();
                editEditor = createInline(
                    row,
                    $("entries").querySelector('[data-row="' + row.id + '"]'),
                );
                editEditor.focus();
            }
        }
        if (del) {
            pendingDelete = rows.find((x) => x.id === del.dataset.delete);
            $("confirm-dialog").showModal();
        }
    }
    if (!admin) {
        $("entries").addEventListener("click", entryAction);
        $("overview-entries").addEventListener("click", entryAction);
        $("clock-in").addEventListener("input", roundPreview);
        async function saveDailyDetails() {
            if (
                dailySaving ||
                loading ||
                !day?.canEdit ||
                newEditor?.busy ||
                editEditor?.busy ||
                drawer?.busy
            )
                return;
            if (!dailyDirty) return;
            dailySaving = true;
            newEditor?.refresh();
            try {
                do {
                    const name = $("day-name").value.trim(),
                        time = $("clock-in").value;
                    if (!name || !$("day-form").checkValidity()) {
                        dailyDirty = true;
                        $("day-status").textContent =
                            "Not saved. Enter a display name and valid clock-in time.";
                        return;
                    }
                    if (
                        day.id &&
                        name === day.displayName &&
                        time.slice(0, 5) === day.clockInTime?.slice(0, 5)
                    ) {
                        dailyDirty = false;
                        $("day-status").textContent = "Daily details saved automatically.";
                        return;
                    }
                    dailyDirty = true;
                    $("day-status").textContent = "Saving daily details…";
                    error("page-error", "");
                    day = await api("/api/timesheets/day", "PUT", {
                        workDate: selectedDate,
                        clockInTime: time.length === 5 ? time + ":00" : time,
                        displayName: name,
                        version: day.version,
                        minutesPerUnit: day.minutesPerUnit,
                    });
                    dailyDirty =
                        $("day-name").value.trim() !== name || $("clock-in").value !== time;
                } while (dailyDirty);
                if (!(await load({ month: false }))) {
                    $("day-status").textContent =
                        "Daily details saved, but the timeline could not refresh. Reload this page.";
                    return;
                }
                // Inputs remain available during a request; save any changes made while refreshing too.
                dailyDirty =
                    $("day-name").value.trim() !== day.displayName ||
                    $("clock-in").value.slice(0, 5) !== day.clockInTime?.slice(0, 5);
                $("day-status").textContent = dailyDirty
                    ? "Saving daily details…"
                    : "Daily details saved automatically.";
            } catch (ex) {
                dailyDirty = true;
                $("day-status").textContent =
                    "Not saved. " + ex.message + " Leave the field again or press Enter to retry.";
                return;
            } finally {
                dailySaving = false;
                newEditor?.refresh();
            }
            if (dailyDirty) saveDailyDetails();
        }
        for (const input of [$("day-name"), $("clock-in")]) {
            input.addEventListener("input", () => {
                dailyDirty = true;
                $("day-status").textContent = "Unsaved changes — saves when you leave the field.";
                newEditor?.refresh();
            });
            input.addEventListener("blur", saveDailyDetails);
        }
        $("day-form").addEventListener("submit", (e) => {
            e.preventDefault();
            saveDailyDetails();
        });
        $("day-form").addEventListener("keydown", (e) => {
            if (e.key === "Enter") {
                e.preventDefault();
                saveDailyDetails();
            }
        });
        const picker = taskPicker($("task-search"), $("task-options"), (t) => {
            const rule = t?.descriptionRule || 0;
            $("files-label").hidden = rule !== 2;
            $("files-closed").required = rule === 2;
            $("description").required = rule === 1;
            $("description-hint").textContent = rule === 1 ? "required" : "optional";
        });
        drawer = { busy: false, row: null, date: today, submissionId: null, start: 0, picker };
        wholeNumberInput($("units"));
        $("units").addEventListener("input", drawerPreview);
        $("entry-clock-in").addEventListener("input", () => {
            const p = $("entry-clock-in").value.split(":").map(Number);
            const n = p[0] * 60 + p[1];
            if (Number.isFinite(n)) {
                drawer.start = n;
                drawerPreview();
            }
        });
        $("calendar-add").addEventListener("click", () => openDrawer(null));
        $("entry-dialog").addEventListener("cancel", (e) => {
            if (drawer.busy) e.preventDefault();
        });
        $("entry-form").addEventListener("submit", async (e) => {
            e.preventDefault();
            if (drawer.busy) return;
            $("task-search").setCustomValidity(
                picker.value ? "" : "Select a task type from the results.",
            );
            if (!$("entry-form").reportValidity()) return;
            drawer.busy = true;
            e.submitter.disabled = true;
            $("entry-form")
                .querySelectorAll("[data-close]")
                .forEach((b) => (b.disabled = true));
            error("entry-error", "");
            try {
                if (!day.id) {
                    const time = $("entry-clock-in").value;
                    day = await api("/api/timesheets/day", "PUT", {
                        workDate: drawer.date,
                        clockInTime: time.length === 5 ? time + ":00" : time,
                        displayName: $("entry-day-name").value,
                        minutesPerUnit: day.minutesPerUnit,
                    });
                    $("entry-day-setup").hidden = true;
                    [$("entry-day-name"), $("entry-clock-in")].forEach((x) => {
                        x.required = false;
                        x.disabled = true;
                    });
                }
                const row = drawer.row;
                await api("/api/timesheets" + (row ? "/" + row.id : ""), row ? "PUT" : "POST", {
                    workDate: drawer.date,
                    units: Number($("units").value),
                    taskTypeId: picker.value.id,
                    description: $("description").value,
                    filesClosed: $("files-label").hidden ? null : Number($("files-closed").value),
                    submissionId: drawer.submissionId,
                    version: row?.version ?? null,
                    dayVersion: day.version,
                });
                $("entry-dialog").close();
                await load();
                toast("Task saved. Timeline updated.");
            } catch (ex) {
                error("entry-error", ex.message);
            } finally {
                drawer.busy = false;
                e.submitter.disabled = false;
                $("entry-form")
                    .querySelectorAll("[data-close]")
                    .forEach((b) => (b.disabled = false));
            }
        });
        $("confirm-form").addEventListener("submit", async (e) => {
            e.preventDefault();
            if (e.submitter.disabled) return;
            e.submitter.disabled = true;
            try {
                await api("/api/timesheets/" + pendingDelete.id, "DELETE", {
                    version: pendingDelete.version,
                    dayVersion: day.version,
                });
                $("confirm-dialog").close();
                await load();
            } catch (ex) {
                $("confirm-dialog").close();
                error("page-error", ex.message);
            } finally {
                e.submitter.disabled = false;
            }
        });
    } else {
        $("reopen-day").addEventListener("click", () => {
            $("reopen-form").reset();
            $("reopen-dialog").showModal();
        });
        $("reopen-form").addEventListener("submit", async (e) => {
            e.preventDefault();
            e.submitter.disabled = true;
            try {
                await api("/api/timesheets/reopen", "POST", {
                    employeeId: $("employee-filter").value,
                    workDate: selectedDate,
                    reason: $("reopen-reason").value,
                    version: day.version,
                });
                $("reopen-dialog").close();
                await load();
            } catch (ex) {
                $("reopen-dialog").close();
                error("page-error", ex.message);
            } finally {
                e.submitter.disabled = false;
            }
        });
    }
    function drawerPreview() {
        preview($("units"), drawer.start, $("entry-timeline"), $("duration"), drawer.row?.minutesPerUnit ?? day.minutesPerUnit);
    }
    function openDrawer(row) {
        if (
            admin ||
            mode !== "calendar" ||
            loading ||
            !day?.canEdit ||
            day.workDate !== selectedDate
        )
            return;
        $("entry-form").reset();
        error("entry-error", "");
        drawer.row = row;
        drawer.date = selectedDate;
        drawer.submissionId = crypto.randomUUID();
        drawer.start = row?.startMinute ?? day.nextStartMinute;
        $("entry-title").textContent = row ? "Edit task" : "Add task";
        $("entry-work-date").textContent = dateLabel(selectedDate);
        $("entry-day-summary").textContent =
            day.totalUnits + " units already recorded · " + hours(day.totalUnits * day.minutesPerUnit) + " elapsed";
        $("units").value = row?.units ?? "";
        $("description").value = row?.description || "";
        $("files-closed").value = row?.filesClosed ?? "";
        drawer.picker.set(row ? typeFor(row) : null);
        $("entry-day-setup").hidden = !!day.id;
        $("entry-day-name").value = day.displayName;
        $("entry-clock-in").value = day.clockInTime || "";
        [$("entry-day-name"), $("entry-clock-in")].forEach((x) => {
            x.required = !day.id;
            x.disabled = !!day.id;
        });
        drawerPreview();
        $("entry-dialog").showModal();
        $(day.id ? "task-search" : "entry-clock-in").focus({ preventScroll: true });
    }
    async function initialize() {
        try {
            const data = await Promise.all([
                api("/api/task-types" + (admin ? "?includeInactive=true" : "")),
                admin ? api("/api/employees") : null,
            ]);
            types = data[0];
            $("type-filter").innerHTML += types
                .map(
                    (t) =>
                        '<option value="' +
                        t.id +
                        '">' +
                        esc(t.name) +
                        (t.isActive ? "" : " (inactive)") +
                        "</option>",
                )
                .join("");
            if (admin)
                $("employee-filter").innerHTML += data[1]
                    .map((e) => '<option value="' + e.id + '">' + esc(e.displayName) + "</option>")
                    .join("");
            applyMode();
            await load();
        } catch (ex) {
            error("page-error", ex.message);
            setBusy(false);
        }
    }
    initialize();
};
