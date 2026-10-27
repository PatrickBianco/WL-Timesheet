# UI and UX

Visual and interaction rules for the implemented Websters Lawyers timesheet. Behavior and permissions are in `PRD.md`. This is the current interface, not a proposal for a second design system.

There is no manager approval screen. Team overview is the review surface.

## 1. Goals

- A solicitor can log a day from the keyboard without hunting through cards.
- The same workspace serves one person and the team. Administration adds filters and actions; it does not invent a second timesheet.
- Density suits an RDS desktop. Avoid large empty heroes and one-field cards.
- Status, errors, and read-only state are visible before a save fails.
- The firm name and logo are recognizable; the chrome stays quiet.

## 2. Brand and visual system

Observed in `wwwroot/css/site.css` and `Views/Shared/_Layout.cshtml`.

| Token | Value | Use |
|---|---|---|
| `--ink` | `#172b42` | Text |
| `--muted` | `#586c82` | Secondary text |
| `--accent` | `#004080` | Primary actions, links, focus context |
| `--pale` | `#eaf3fc` | Selected and hover surfaces |
| `--border` | `#dfe8f2` | Dividers and inputs |
| `--paper` | `#f7faff` | Sidebar |
| `--red` | `#a33737` | Errors and destructive emphasis |
| Page background | `#fff` | Main canvas |
| Focus | `#75a9df`, 3px, offset 3px | Keyboard focus |

Typography: Inter, Segoe UI, Arial at 14px. Headings are 32px (page) and 17px (section), weight 600. The wordmark in the sidebar uses the Websters Lawyers logo image. Do not invent a new logo or palette.

Eyebrows are short uppercase labels ("MAKE EVERY DAY COUNT", "THE BIGGER PICTURE", "ADMINISTRATION"). Buttons are 12px, weight 600, min-height 42px. Primary buttons are white on `--accent`. Secondary buttons are pale blue. Icons are local Lucide SVGs used as masks, 18px.

Spacing: sidebar 240px; page content sits to the right of it. Inputs are full width of their label, min-height 43px, radius 5px. Do not add a new spacing scale.

## 3. Shell

```text
+----------------+----------------------------------------------+
| Logo           | Workspace / {page title}     WEBSTERS LAWYERS|
| WORKSPACE      |----------------------------------------------|
| My timesheet   | Main                                         |
| ADMINISTRATION |                                              |
| (if unlocked)  |                                              |
| Team overview  |                                              |
| Employees      |                                              |
| Task types     |                                              |
| Timesheet      |                                              |
| settings       |                                              |
| Database backup|                                              |
| Change password|                                              |
| Person + lock  | Footer: Websters Lawyers                     |
+----------------+----------------------------------------------+
```

When administration is locked, the sidebar shows Administration, which opens the unlock form. Lock administration posts to `/account/admin-lock` and stays on the Windows session.

Below 780px the sidebar collapses behind a Menu button. The skip link "Skip to content" is first in the tab order.

Client Review shows a persistent notice: sample data only.

## 4. Screens

### 4.1 My timesheet (`/`) — TS-003, TS-004, TS-008

**Purpose.** Log and review the signed-in person's days.

**Layout.** Page heading, List/Calendar toggle, error notice, one panel.

List toolbar: previous, title, next, Today, work date, task type, Apply filters, then "DAILY TIMESHEET" with Excel and CSV.

Header fields inside the panel: display name, clock-in, status line, rounded-start note (clock-in is shown as entered; the note must not claim a round-down). Saving is on blur or Enter.

Column headings: Time / Duration, Units, Task type, Description, Actions. Saved rows, then one new row, then a one-line keyboard hint, then pagination.

Footer of the panel: result count and the unit caption. Four totals sit under the panel, not above the entry row.

**Interactions.**

- List is the default. Calendar hides the list and shows the month plus a day overview.
- The new row is outside pagination so it stays put.
- Tab order on a new row: Units, Task type, Description, then Enter saves. File-closure types insert the count before the final save.
- Task picker options read `Name (Explanatory notes)`. Notes wrap. There is no second note under the field.
- Read-only days replace the new row with a short message to ask an administrator.
- Loading text is "Updating timesheet…" and must not collapse the calendar.

### 4.2 Calendar — TS-008

Month grid Monday–Sunday. Each day can show a plus control when that day is editable. Plus opens the task drawer for that day. Selecting the day updates the overview beside the grid. Overview has its own previous/next for that day's rows. Export actions can move into the overview in calendar mode.

The drawer is the only modal entry surface. List mode does not use it.

### 4.3 Team overview (`/admin`) — TS-007, TS-010

Same panel as My timesheet with these differences:

- Heading "Team overview".
- Employee select, date from, and date to.
- No personal header form and no new row.
- Reopen selected day appears when a single employee and day are in context. The dialog asks for a reason (required, 500 characters) and states the reopen length from configuration.
- Totals describe the filtered team selection.
- A second panel lists daily and weekly productive totals.
- Excel and CSV call the admin export.

### 4.4 Employees and task types — TS-011, TS-012

`/employees` and `/task-types` share `Management.cshtml`.

Search, paged table, and a drawer to edit the selected record. Employees: display name, email, active. Task types: name, notes, active, break, description rule. Primary action is in the drawer footer. Search resets to page 1.

Empty search shows the paged directory, not an illustration. No rows shows a one-line empty message in the table area.

### 4.5 Timesheet settings (`/settings`) — TS-013

Narrow form card: minutes per unit, help text with the 1-minute and 6-minute examples, and a note that existing days are unchanged. One primary button: Save settings. Success is a notice, then the form reloads with a new rowversion.

### 4.6 Database backup (`/settings/backup`) — TS-014

Wider card (max 640px). Status lines for last backup, next backup, and file count. Error notice when the last attempt failed.

Fields: automatic on/off, days between backups, days to keep, folder. Help text states the weekly and 30-day examples, the retention rule, and that SQL Server must be able to write the folder. Save backup settings, then a separate Back up now button so a click does not also save unsaved edits. The note under Back up now says to save first.

### 4.7 Unlock and password — TS-002

Centered card. Unlock: one password field and Unlock administration. Password change: current, new, confirm. Errors stay in the card. After a successful change, the unlock page states that previous sessions are locked.

### 4.8 Access denied

Short card. Tells the person the Windows account may be inactive or sign-in failed, and to contact a WL administrator. No retry loop and no password field.

## 5. Components

| Component | Rule |
|---|---|
| Primary button | One per form, accent fill, label is a verb: Save settings, Reopen, Unlock administration |
| Secondary button | Pale fill: Apply filters, Excel, CSV, Today, Back up now, Cancel |
| Icon button | Previous, next, paging. `aria-label` required |
| Notice | Neutral for success, `.notice.error` for failure, `role="alert"` or `role="status"` |
| Dialog | Reopen and the calendar task drawer. Cancel does not save. Escape closes |
| Checkbox | Label and box on one row. Used for active, break, and automatic backup |
| Field note | 11px muted text directly under the control it describes |
| Pagination | Page size, range, previous, page number, next |
| Toast | Short confirmation that does not replace a field error |
| Totals | Four equal articles under the panel. Numbers, not charts |

Do not place a primary and a destructive action in the same visual weight. Delete stays on the row.

## 6. States

| State | UI |
|---|---|
| Loading | "Updating timesheet…" or an equivalent status. Layout does not jump |
| Empty day | Header fields plus the new row. Totals show zero or an em dash until loaded |
| Empty filter | No rows, totals for the empty selection, filters remain |
| Saving header | Status line: saving, saved, or the server message. Typed text is kept on failure |
| Read-only | Inputs disabled or absent. Message names the administrator reopen path |
| Success | Notice or toast. The list shows the saved row |
| Error | Page notice or validation summary. The reference from a 500 is visible |
| Conflict | Ask the person to reload. Do not merge silently |
| Backup off | Say that automatic backups are off and that Back up now still uses the saved folder |

## 7. Responsive behavior

| Width | Behavior |
|---|---|
| ≥ 1201px | Sidebar plus a full list. Calendar is month plus side overview |
| 761–1200px | Filters wrap. List columns remain readable; horizontal scroll is preferred to hiding Description |
| ≤ 780px | Sidebar becomes a menu. Heading and toolbar stack |
| ≤ 600px | Day fields stack. Dialog actions stack |
| ≤ 380px | Type and spacing remain at least the body size. Do not shrink below the focus outline |

There is no separate tablet product. Pointer and keyboard both work. Touch targets follow the 42px button height.

`prefers-reduced-motion: reduce` disables non-essential animation, including the task drawer.

## 8. Accessibility

- `lang="en"` on the document.
- One `h1` per page. Section titles are `h2`.
- Icon-only controls have accessible names.
- Focus is visible (3px outline). Do not remove it.
- Notices use `role="alert"` or `role="status"`.
- Dialogs expose their title to the accessibility tree.
- Color is not the only signal for errors or read-only state; text states the condition.
- Form controls have visible labels, not placeholder-only labels.
- The timesheet list is keyboard operable without the mouse (TS-004).
- Contrast for body text on white and for white on `#004080` must remain at least WCAG AA for normal text.

## 9. Consistency

- Administration forms use the login card. Workspace screens use the panel. Do not mix a dashboard grid into the timesheet.
- Excel and CSV stay together at the right of the daily heading.
- Dates use `en-AU` formatting in the client.
- Destructive confirmation is the reopen dialog's explicit submit, not a browser `alert`. Backup now is a separate form so it is not accidental with Save.
- New screens follow the sidebar label pattern: short name, existing icon, `active` class from `ViewData["Page"]`.
- Do not add a manager inbox, a kanban board, or a card per timesheet row.
