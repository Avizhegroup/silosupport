# Customer support message classification and routing

Customer messages arrive in Bale support groups. A **Windows service** (`SiloSupport.Service.CustomerSupport`) receives
them, classifies their priority with the existing Silo AI API and forwards the original text plus the AI analysis to the
customer's destination group for that priority. The **Blazor admin site** (`SiloSupport.Web`) only shows and manages the
data (customers, members, priority groups, forwarded messages); it never talks to Bale or the AI API.

```
 Bale support groups ──► SiloSupport.Service.CustomerSupport ──► Silo AI API
                                   │   (Windows service)
                                   ▼
                              SQL Server  ◄──── SiloSupport.Web (Blazor, Telerik)
                                   │                  manage / view / resolve
                                   ▼
                       Bale priority groups (sendMessage)
```

## Projects

| Project | Target | Responsibility |
|---|---|---|
| `SiloSupport.Domain` | net9.0 | `Customer`, `CustomerMember`, `CustomerGroup`, `CustomerGroupedMessage`, `GroupPriority`, `AppRoles` |
| `SiloSupport.Application` | net9.0 | Admin-side ports (`I*Repository`, `ITransactionRunner`), the four admin services, DTOs, `PriorityHelper`. **No Bale / AI / message-processing code** |
| `SiloSupport.Infrastructure` | net9.0 | Only the existing `SiloAiClient` (unchanged) |
| `SiloSupport.Persistence` | net9.0 | `ApplicationDbContext` (Identity + domain), EF configurations, repositories, `EfTransactionRunner`, migrations, `AddSupportPersistence` |
| `SiloSupport.Service.CustomerSupport` | net9.0 | **New Windows service** and the only home of the bot: `BaleUpdatesWorker`, `CustomerMessageProcessor`, `IBaleBotClient`/`BaleBotApiClient`, `IMessagePriorityClassifier`/`SiloAiPriorityClassifier`, AI result parser, prompt, formatter, `BaleBotOptions` |
| `SiloSupport.Web` | net9.0 | Blazor Web App (Interactive Server + **Telerik UI for Blazor**), Identity login, admin pages. References Application + Persistence only |
| `SiloSupport.Tests` | net9.0 | xUnit + SQLite in-memory for Application / Persistence / Infrastructure. **No Web tests** |

Everything targets **net9.0** (set once in `Directory.build.props`), with EF Core and Identity at the same version as the
rest of the solution (`$(AspNetCoreVersion)`). `SiloSupport.Service.CustomerChat` is **unchanged**;
`SiloSupport.Application.csproj` gained a Domain reference, but no source file of the existing service changed.

The Web project contains **no** Bale, AI or message-processing code: no worker, no `CustomerMessageProcessor`, no Bale/AI
options or HTTP clients. It registers only the four admin services it needs. All message handling lives in
`SiloSupport.Service.CustomerSupport`; Web does not reference it.

## Processing flow (`CustomerMessageProcessor`, run by the Windows service)

Updates arrive through **long polling** (`getUpdates`), the same mechanism `SiloSupport.Service.CustomerChat` uses
(`BaleUpdatesWorker`). For every `message` update, in this order:

1. If the originating chat id equals the Bale id of **any** configured `CustomerGroup` → ignore (logged).
2. If the sender is flagged `is_bot`, or equals the bot's own id (`getMe`, cached) → ignore.
3. If the message carries anything other than plain text (voice, audio, photo, document, video, sticker, ...) → ignore
   (logged). Nothing is downloaded or transcribed.
4. Empty / whitespace-only text → ignore.
5. Find the `CustomerMember` by the sender's Bale id; none → ignore (logged). The customer comes from the member.
6. Ask the AI API for the priority (see below). Failure / invalid / unsupported priority → log, stop. No fallback.
7. Resolve the destination group for **that customer and that priority**. Missing → log, stop.
8. Format the text (customer, sender, priority, original message, AI analysis when returned).
9. `sendMessage` to the destination group. Failure → log, **no record is written**.
10. After a successful send, store a `CustomerGroupedMessage` with the exact sent text, the group's internal id,
    `IsResolved = false` and a UTC timestamp. A database failure at this point is logged (the message is in Bale but
    not in the history).

The bot never replies to the source group and never sends error or info messages there.

### How the priority is obtained

`Docs/RAG-Chat-Guide.md` documents only `POST /api/rag/chat/send`, which returns free text (`responseText`); it has no
priority field and no classification endpoint. The adapter therefore sends a classification prompt through that same
documented endpoint (via the existing `ISiloAiClient`, no conversation id, so customers are never mixed) asking for
`{"priority":"Low|Medium|Critical","analysis":"..."}`. `AiPriorityResultParser` accepts **only** those three exact
names (case-insensitive); anything else is rejected. `analysis` is the model's own text; if missing, the section is
omitted. No local keyword rules exist.

**If the AI service exposes a dedicated classification endpoint, replace `SiloAiPriorityClassifier`** (one class
behind `IMessagePriorityClassifier`); nothing else changes.

## Data model and constraints

- `CustomerMember.BaleId`: **text** (`nvarchar(64)`, trimmed). The numeric sender id of an incoming Bale message is compared with it as its invariant decimal string, so a member registered with `123456789` matches sender `123456789`.
- `CustomerGroup.BaleId`: `long` (chat ids can exceed 32 bits, up to 52 significant bits).
- Unique: `CustomerMembers.BaleId`; `CustomerGroups.BaleId`; `CustomerGroups(CustomerId, Priority)`.
- `CustomerGroups.Priority` is stored as text (`Low`/`Medium`/`Critical`).
- `CustomerGroupedMessages.GroupId → CustomerGroups` is `Restrict`: groups and customers with history cannot be
  deleted (the services check first and show a message; the database enforces it). Members and groups without
  history are removed together with their customer.
- Business uniqueness is enforced by the services (friendly messages) **and** by the database.

## Admin UI (`SiloSupport.Web`, Telerik UI for Blazor, Persian / RTL, `[Authorize(Roles = "Administrator")]`)

| Route | Page |
|---|---|
| `/` | **dashboard**: totals, donut chart per priority, columns per customer (top 10), daily line chart (7/30/90 days, Jalali dates), per-customer table |
| `/admin/customers` | list (member count, missing priority groups), delete |
| `/admin/customers/new`, `/{id}/edit` | create / edit |
| `/admin/customers/{id}` | details: members, the three priority slots, missing-group warning |
| `/admin/customers/{id}/members/new`, `/{memberId}/edit` | member form (customer shown explicitly) |
| `/admin/customers/{id}/groups/new`, `/{groupId}/edit` | destination group form |
| `/admin/conversations[/{customerId}]` | forwarded messages of one customer, per group, oldest first, resolve switch |

The pages only use Application services. The conversations page loads one customer's messages with a database filter.
Telerik components used: `TelerikRootComponent`, `TelerikAppBar`, `TelerikDrawer` (sidebar), `TelerikChart`,
`TelerikGrid`, `TelerikTextBox`, `TelerikTextArea`, `TelerikNumericTextBox`, `TelerikDropDownList`, `TelerikSwitch`,
`TelerikButton`, `TelerikDialog` (delete confirmations, `ConfirmDialog.razor`). The login and access-denied pages are
static server-rendered pages (they post a form), so they use plain inputs styled with the Telerik theme classes.

### Persian / RTL

- `App.razor` renders `<html lang="fa" dir="rtl">`; the Telerik theme and layout mirror automatically. Charts and the
  Bale-id / group-id inputs sit in `dir="ltr"` containers so numbers and axes are not mirrored.
- All page texts, validation messages and service messages are Persian. Priorities are shown as کم / متوسط / بحرانی
  (`PriorityHelper.DisplayName`); the stored values stay `Low` / `Medium` / `Critical`.
- Timestamps are shown in Iran time (UTC+03:30) with the Jalali calendar (`PersianDateFormatter`).
- Telerik's own built-in strings come from `Localization/TelerikPersianLocalizer.cs` (registered as
  `ITelerikStringLocalizer`); keys it does not list keep Telerik's default.
- The forwarded text sent to the Bale priority groups is unchanged (it is not part of the admin UI).

### Sidebar and authorization

- The sidebar (`TelerikDrawer` in `MainLayout.razor`) is rendered only inside `AuthorizeView Roles="Administrator"`:
  Dashboard (`/`), Customers, Conversations.
- Application level: a fallback authorization policy requires an authenticated user for every endpoint that is not
  explicitly `[AllowAnonymous]` (login / access denied pages, login and logout endpoints).
- Role level: every admin page, including the dashboard, has `[Authorize(Roles = "Administrator")]`; users without the
  role are redirected to the access-denied page.

### Dashboard data

`DashboardService` reads the forwarded-message history (`CustomerGroupedMessages` joined to their group and customer):
all-time counts per customer / priority / resolution state are aggregated in the database; the daily series loads only
the creation time and priority of messages inside the selected window and buckets them per Iran-local day. Customers
without messages are listed with zeros.

Telerik UI for Blazor is a commercial product: `Telerik.UI.for.Blazor` (version pinned in `Directory.Packages.props`)
must be restorable from your Telerik feed / local package folder in `NuGet.Config`, and a Telerik license key must be
installed on the build machine (see Telerik's licensing documentation), otherwise the build shows a license warning.

## Configuration

All settings are plain values in the `appsettings.json` of each host (no user secrets, no environment variables).

**Windows service** (`SiloSupport.Service.CustomerSupport/appsettings.json`):

```json
{
  "Database": { "ConnectionString": "..." },
  "Bale": { "Enabled": true, "BotToken": "<bot token from @botfather>", "ApiBaseUrl": "https://tapi.bale.ai", "LongPollTimeoutSeconds": 30 },
  "SiloAI": { "BaseUrl": "http://localhost:5300/", "ApiKey": "<Silo AI API key>", "TopK": 5, "IsMainChat": true, "DocType": "GeneralChat", "Key": null }
}
```

**Web** (`SiloSupport.Web/appsettings.json`):

```json
{
  "Database": { "ConnectionString": "..." },
  "AdminSeed": { "UserName": "<admin user name>", "Password": "<admin password>" }
}
```

- `Database:ConnectionString` must point to the same database in both files.
- `Bale:BotToken` and `SiloAI:ApiKey` are empty in the repository: fill them in before starting the service. The worker
  does not start (it logs a warning) while `Bale:BotToken` is empty, and `Bale:Enabled=false` stops it as well.
- `AdminSeed` creates the first administrator at startup when both values are set and the user does not exist yet; with
  empty values no administrator is created. Choose your own credentials; no default account is shipped.
- The Bale bot token is part of the Bale request URL, so `System.Net.Http.HttpClient` logging is kept at `Warning`
  (already set) to avoid writing it to logs. The service logs with Serilog to the console and to
  `logs/customer-support-.log` next to the executable.
- Because these files hold real credentials, keep the repository private.

Group chat ids: add the bot to each destination group; the chat id is the numeric `chat.id` Bale reports (usually
negative for groups).

### Installing the Windows service

```
dotnet publish SiloSupport.Service.CustomerSupport -c Release -o C:\Services\SiloCustomerSupport
sc.exe create "Silo Customer Support Service" binPath= "C:\Services\SiloCustomerSupport\SiloSupport.Service.CustomerSupport.exe" start= auto
```

Edit `appsettings.json` next to the published executable (connection string, bot token, AI key) before starting it.

## Migrations

The **Web project owns and applies the migrations** at startup (`Database.MigrateAsync()`); the Windows service never
migrates, so start the Web site (or run `dotnet ef database update`) before the service.

Migrations (both written by hand together with the model snapshot):

1. `20261027090000_InitialCreate` – Identity + support tables.
2. `20261028100000_MemberBaleIdAsString` – `CustomerMembers.BaleId` from `bigint` to `nvarchar(64)`; existing numeric ids are
   converted to their text, the unique index is recreated. SQL Server uses `AlterColumn`; the SQLite branch (tests only)
   replaces the column with plain SQL. `Down` converts back and fails if a member has a non-numeric id.

They were written by hand because it was authored in an environment without the .NET SDK. Before relying on it, from the
solution root run:

```
dotnet tool restore --tool-manifest SiloSupport.Web/.config/dotnet-tools.json
dotnet ef migrations has-pending-model-changes -p SiloSupport.Persistence -s SiloSupport.Web
```

If it reports differences (for example the Identity schema version), delete the two migration files and regenerate:

```
dotnet ef migrations add InitialCreate -p SiloSupport.Persistence -s SiloSupport.Web
```

`MigrationTests` applies the chain to SQLite, including a data-conversion test (numeric id → text, unique index kept).

## Tests

`dotnet test SiloSupport.slnx` — covers priority parsing, the dashboard statistics (totals, per-customer, daily buckets in Iran time, window boundaries), Persian date/priority formatting, the text Bale id (validation, storage, matching a numeric sender), the prompt and formatter, all routing rules (fakes for
repositories, Bale and the AI API), the Bale and AI adapters (stub handlers, no network), EF constraints / repositories /
services (SQLite in-memory) including the conversation queries and the resolve flag, and the migration chain.
The Blazor Web project has no tests by decision.

## Known limitations

- Only `message` updates are handled (not `edited_message`); media and captions are ignored by design.
- The worker starts at offset 0 and acknowledges a batch on the next poll, so after a restart Bale may re-deliver the
  last batch it had not yet seen acknowledged (and anything older than that, up to 24 h, if the bot was offline). A
  message already forwarded just before a restart could therefore be forwarded twice.
- Messages are processed concurrently (max 5), so forwarded order across messages is not guaranteed.
- `SiloSupport.Service.CustomerChat/appsettings.json` contains what look like a real Bale bot token and AI API key.
  That project is read-only for this task; make sure the repository stays private (or rotate those credentials).
