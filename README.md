# PermitTrack

A building permit and inspection tracking system for a local government building department. Contractors and homeowners apply for permits online, permit office staff review applications and collect fees, and field inspectors work their daily route and record results from a phone.

Built with **ASP.NET Core 10 (Razor Pages)**, **Entity Framework Core**, and **SQLite**, with an xUnit test suite.

> All people, addresses, permit numbers and fee amounts are fictional demo data.

## What it does

| Role | Can do |
|---|---|
| **Applicant** (contractor / homeowner) | Start an application with a live fee estimate, upload plans, submit, respond to correction requests, request inspections, see what needs attention |
| **Clerk** (permit office) | Work the review queue (with overdue flags), request corrections, record fee payments, approve/issue or reject, export CSV |
| **Inspector** | See today's route and upcoming inspections, record pass / fail / cancel with notes and GPS location on a mobile-friendly form |
| **Admin** | Everything staff can do, plus record results on any inspection |

### Personalization
Each user has their own settings (gear icon in the top bar), saved to the database:
- **Color themes** (Navy, Forest, Plum, Slate, Sunset, Teal), **light / dark / match device**, and **comfortable / compact density**. All are built on CSS custom properties, and the settings page previews changes live.
- **Dashboard layout**: choose 1–3 columns, show or hide each widget, set widgets to normal or full-row width, and reorder by drag-and-drop or the arrow buttons. A mini map previews the layout. Widgets can also be hidden straight from the dashboard with ×.
- Widgets are defined in a catalog ([`UserSettings.cs`](src/PermitTrack.Web/Domain/UserSettings.cs)) with the roles allowed to see them. Saved layouts are normalized against it, so new widgets show up for existing users and widgets a role can't use never appear.

### Business rules modeled
- **Permit lifecycle as a state machine**: Draft → Submitted → Under Review → Issued → Finaled, with a Corrections Required loop, Rejected and Withdrawn exits. Each transition is limited to specific roles ([`PermitWorkflow.cs`](src/PermitTrack.Web/Domain/PermitWorkflow.cs)).
- **Preconditions**: fees must be paid before a permit can be issued, and plans must be attached before a non-demolition application can be submitted.
- **Valuation-based fee schedule** with tiered per-$1,000 rates, "or fraction thereof" rounding, and a plan-review surcharge over $10k ([`FeeCalculator.cs`](src/PermitTrack.Web/Domain/FeeCalculator.cs)).
- **Inspection sequencing**: each permit type has a required inspection sequence, such as footing → framing → insulation → final. The next inspection unlocks only after the previous one passes. Failed inspections can be re-requested. The permit **auto-finals** when the last required inspection passes ([`InspectionRules.cs`](src/PermitTrack.Web/Domain/InspectionRules.cs)).
- **180-day expiration**: issued permits lapse 180 days after issuance or the last passed inspection (the common IRC/IBC rule). The dashboard flags permits expiring within 30 days.
- **Review targets**: applications waiting more than 7 days for review are flagged as overdue.
- **Inspector load balancing**: new inspections go to the inspector with the fewest bookings that day.
- **Audit trail**: every state change, payment, upload and inspection result is recorded with who and when.

### Security and data-handling choices
- Authorization is enforced on the server in the service layer, not just by hiding buttons. Applicants get a 404 for other people's permits, so permit IDs can't be probed.
- Uploads are restricted by extension and size and stored under random names outside `wwwroot`. They are only served through an authorized handler.
- CSV export escapes formula characters to prevent spreadsheet formula injection.
- All forms use ASP.NET Core's built-in anti-forgery tokens.

## Running it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/PermitTrack.Web
```

Open http://localhost:5030 and pick a demo user to sign in as. The SQLite database is created and seeded on first run in `src/PermitTrack.Web/App_Data/`. To reset the data, delete that folder. Seed dates are relative to today, so the dashboard always shows overdue reviews, today's inspections and permits near expiration.

```bash
dotnet test
```

### Desktop launcher (Windows)
`scripts/publish.ps1` builds a release copy into a git-ignored `app` folder in this repo and creates a **PermitTrack Demo** shortcut on the desktop. The shortcut asks how to open the app:
- **Desktop app window**: opens in its own Edge (or Chrome) app window. The server runs hidden and stops when you close the window.
- **Web browser (localhost)**: opens http://localhost:5080 in your default browser. The server runs in a minimized console window; close that window to stop it.

`app\Start-PermitTrack.ps1 -Mode app|browser` skips the question. After changing code, run the publish script again. `scripts/make-icon.ps1` regenerates the icon (`wwwroot/favicon.ico`).

## Project layout

```
src/PermitTrack.Web/
  Domain/      Entities, enums, and pure business rules (fees, workflow, inspections) with no I/O
  Data/        EF Core DbContext and seed data
  Services/    PermitService: every state change goes through here (rules + audit trail + persistence)
  Pages/       Razor Pages UI (Account, Dashboard, Permits, Inspections)
tests/PermitTrack.Tests/
  Unit tests for the domain rules, plus service tests against in-memory SQLite
```

## Design notes / what I'd do next
- **Sign-in is a demo user picker** so reviewers can switch roles instantly. Everything downstream depends only on standard role claims, so swapping in ASP.NET Core Identity or Microsoft Entra ID is a change to `Program.cs` and the login page.
- **SQLite with `EnsureCreated`** keeps setup to one command. For production I'd move to SQL Server with EF Core migrations.
- Next features: online payments, email/SMS notifications when status changes, a map view of the day's inspection route, offline support for inspectors (PWA), and a public permit lookup page.
