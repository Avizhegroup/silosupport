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
3. If the sender's Bale user name belongs to a **support person** (`SupportPersons.BaleUsername`, ignoring case and a
   leading `@`) → ignore: our own team's messages are never customer messages. **Every other sender is a customer.**
4. The text of the message is its `text` or, for a file, its `caption`. A message that has neither text nor a supported
   file (photo, video, voice, audio, document, animation) – a sticker, contact or location, say – → ignore (logged).
5. Empty / whitespace-only text without a file → ignore.
6. Resolve the customer: (a) a registered `CustomerMember` whose Bale ID equals the sender's numeric id or user name
   wins, even when the message came from another customer's chat; (b) otherwise the customer whose
   `Customers.BaleChatId` is the chat the message came from, so senders without a user name, or nobody registered, are
   still processed; (c) otherwise ignore (logged with a hint to register the member or set the customer's chat id).
7. Load the customer's **open issues** (see "Issue threads" below) and ask the AI API for the priority **and** whether the
   message continues one of them (see below). Failure / invalid / unsupported priority → log, stop. No fallback.
   A message that is only a file (no text) is never filed as Low: it is raised to Medium, because support cannot judge it.
8. Resolve the destination group for **that customer and that priority**. Missing → log, stop.
9. Format the text (customer, sender, priority, issue title / "continues issue", attachment kind and file name, original
   message, AI analysis when returned).
10. Send to the destination group: a file is re-sent **by its `file_id`** (`sendPhoto`, `sendVideo`, `sendVoice`,
    `sendAudio`, `sendAnimation` or `sendDocument`) with the formatted text as the caption. If Bale refuses the file the
    text is sent as a normal message with a note that the file could not be sent. Failure → log, **no record is written**.
11. After a successful send, download the file (`getFile`, up to `Bale:MaxDownloadMegabytes`, default 20 MB, the Bale
    limit) and store a `CustomerGroupedMessage` with the exact sent text, the customer's original text, the thread, the
    group's internal id, `IsResolved = false`, a UTC timestamp and a `MessageAttachment` (kind, name, MIME type, size,
    Bale `file_id` and the bytes). A file that is too large or cannot be downloaded is still forwarded and recorded, just
    without bytes. A database failure at this point is logged (the message is in Bale but not in the history).

The Bale download URL contains the bot token and is never logged.

The bot never replies to the source group and never sends error or info messages there.

### Issue threads (follow-up messages)

Customers often describe one problem over several messages, and often send unrelated messages one after another. Every
recorded message therefore belongs to a `SupportThread` (one issue of one customer):

- Before classifying, the service loads the customer's **open** threads: at least one unresolved message and activity
  within `Threads:OpenWindowHours` (default 24), at most `Threads:MaxThreadsInContext` (5), each with its last
  `Threads:MessagesPerThread` (4) customer texts and the priority they got.
- These are put in the prompt, and the model answers `{"priority", "analysis", "threadId", "title"}`: `threadId` is the
  number of the open issue the new message **clearly** continues, or `null` for a new, unrelated issue.
- Only an id that was actually offered (same customer) is accepted; anything else starts a new issue (logged).
- A follow-up is still routed by **its own** priority, but the model is told to judge the whole issue with its history
  (a short "still not working" after a critical outage stays Critical). The forwarded text says "ادامه موضوع: <title>"
  or "موضوع جدید: <title>".
- Without an AI title the issue is titled with an excerpt of the first message. If the history cannot be loaded the
  message is classified as a new issue.
- Messages recorded before this feature have no thread and are shown as "old messages" in the panel.

### How the priority is obtained

`Docs/RAG-Chat-Guide.md` documents only `POST /api/rag/chat/send`, which returns free text (`responseText`); it has no
priority field and no classification endpoint. The adapter therefore sends a classification prompt through that same
documented endpoint (via the existing `ISiloAiClient`, no conversation id, so customers are never mixed) asking for
`{"priority":"Low|Medium|Critical","analysis":"...","threadId":<number|null>,"title":"..."}`. `AiPriorityResultParser` accepts **only** those three exact
names (case-insensitive); anything else is rejected. `analysis` is the model's own text; if missing, the section is
omitted. No local keyword rules exist.

**If the AI service exposes a dedicated classification endpoint, replace `SiloAiPriorityClassifier`** (one class
behind `IMessagePriorityClassifier`); nothing else changes.

## Data model and constraints

- `SupportPerson` (`SupportPersons`): `Id`, `Name`, `PhoneNumber`, `BaleUsername` (stored normalized: no `@`, lower case; unique). The team is shared by all customers and managed on `/admin/support-persons`.
- `Customer.BaleChatId`: optional `long`, unique when set (filtered index). The Bale chat (usually a group) where a customer's people write; it must not be one of the priority groups.
- `CustomerMember.PhoneNumber` is required (at least 10 digits; Persian digits accepted). `CustomerMember.BaleId` is **optional** (people without a Bale user name). When set it is **text** (`nvarchar(64)`, trimmed). Administrators enter the Bale **user name** (`@ali`) or the numeric Bale user id. An incoming message is matched on the numeric `from.id` **or** the `from.username`, ignoring case and a leading `@` (`@Ali`, `ali` and `ALI` are the same). A numeric id match wins over a user name match. If the match spans members of more than one customer, the message is ignored with a warning. Bale messages never carry the sender's phone number, so it cannot be used for matching; members without a Bale ID are covered by the customer's chat id (flow step 6b).
- `CustomerGroup.BaleId`: `long` (chat ids can exceed 32 bits, up to 52 significant bits).
- `SupportThread` (`SupportThreads`): `CustomerId` (cascade), `Title`, `CreatedAtUtc`, `LastActivityUtc`. `CustomerGroupedMessages` gain `ThreadId` (nullable, `ON DELETE SET NULL`) and `OriginalText` (what the customer wrote; the forwarded `Message` also contains our header).
- `MessageAttachment` (`MessageAttachments`): one per message (unique `GroupedMessageId`, cascade), `Kind` (text), `FileName`, `MimeType`, `SizeBytes`, `BaleFileId`, `BaleFileUniqueId`, `Content` (`varbinary(max)`, NULL when not downloaded). The list queries never load `Content`.
- Unique: `SupportPersons.BaleUsername`; `Customers.BaleChatId` and `CustomerMembers.BaleId` (only non-NULL values); `CustomerGroups(CustomerId, Priority)`. `CustomerGroups.BaleId` is **not** unique any more (migration `Remove_Unique_BaleId`).
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
| `/admin/support-persons`, `/new`, `/{id}/edit` | support team: list (Id, name, phone, Bale user name), create, edit, delete |
| `/admin/conversations[/{customerId}]` | one customer's conversation in two tabs: **issues** (threads with their messages, priority, files, resolve a whole issue) and **groups** (per priority group, resolve a message) |
| `/admin/attachments/{id}` | the stored file (endpoint, Administrator only) |

**Files in the panel**: images, video and audio of allow-listed types play inline (`<img>`, `<video>`, `<audio>`); every other
type – and every download button – is served as `application/octet-stream` with a sanitized file name. The endpoint
sends `X-Content-Type-Options: nosniff` and a sandboxing CSP, requires the Administrator role, and never touches the Bale
URL, so an uploaded HTML/SVG file cannot run in the panel's origin.

The pages only use Application services. The conversations page loads one customer's messages with a database filter.
Telerik components used: `TelerikRootComponent`, `TelerikAppBar`, `TelerikDrawer` (sidebar), `TelerikChart`,
`TelerikGrid`, `TelerikTextBox`, `TelerikTextArea`, `TelerikNumericTextBox`, `TelerikDropDownList`, `TelerikSwitch`,
`TelerikButton`, `TelerikDialog` (delete confirmations, `ConfirmDialog.razor`). The login and access-denied pages are
static server-rendered pages (they post a form), so they use plain inputs styled with the Telerik theme classes.

### Persian / RTL

- `App.razor` renders `<html lang="fa" dir="rtl">`; the Telerik theme and layout mirror automatically. Charts and the
  Bale-id / group-id inputs sit in `dir="ltr"` containers so numbers and axes are not mirrored.
- RTL is applied at several levels so no component can fall back to LTR: `<html lang="fa" dir="rtl">`,
  `<body dir="rtl">`, the `app-root k-rtl` wrapper inside `TelerikRootComponent`, and `direction: rtl` rules in `app.css`.
- Font: **Vazirmatn** (SIL OFL 1.1) is bundled in `wwwroot/fonts` (Regular, Medium, Bold + licence) and set as the page
  font and as `--kendo-font-family`, so Telerik components and charts use it too. No CDN is needed.
- The sidebar drawer starts **closed** and opens over the page (overlay) from the menu button; it closes again after
  navigation.
- All page texts, validation messages and service messages are Persian. Priorities are shown as کم / متوسط / بحرانی
  (`PriorityHelper.DisplayName`); the stored values stay `Low` / `Medium` / `Critical`.
- Timestamps are shown in Iran time (UTC+03:30) with the Jalali calendar (`PersianDateFormatter`).
- Telerik's own built-in strings come from `Localization/TelerikPersianLocalizer.cs` (registered as
  `ITelerikStringLocalizer`); keys it does not list keep Telerik's default.
- The forwarded text sent to the Bale priority groups is unchanged (it is not part of the admin UI).

### Sidebar and authorization

- The sidebar (`TelerikDrawer` in `MainLayout.razor`) is rendered only inside `AuthorizeView Roles="Administrator"`:
  Dashboard (`/`), Customers, Conversations, Support team.
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

**Windows service** (`SiloSupport.Service.CustomerSupport/appsettings.json`; `Bale:MaxDownloadMegabytes` (20) and the
`Threads` section (`OpenWindowHours` 24, `MaxThreadsInContext` 5, `MessagesPerThread` 4) are optional):

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

Migrations (all written by hand together with the model snapshot):

1. `20261027090000_InitialCreate` – Identity + support tables.
2. `20261028100000_MemberBaleIdAsString` – `CustomerMembers.BaleId` from `bigint` to `nvarchar(64)`; existing numeric ids are
   converted to their text, the unique index is recreated. SQL Server uses `AlterColumn`; the SQLite branch (tests only)
   replaces the column with plain SQL. `Down` converts back and fails if a member has a non-numeric id.
3. `20261103100000_MemberBaleIdOptional` – `CustomerMembers.BaleId` becomes nullable (empty values become NULL) and its
   unique index is filtered to non-NULL values. `Down` gives such members a `member-<id>` placeholder.
4. `20261115100000_SupportPersonsAndCustomerChat` – new `SupportPersons` table (unique `BaleUsername`) and the nullable
   `Customers.BaleChatId` with a filtered unique index.
5. `20261103074459_Remove_Unique_BaleId` – generated by `dotnet ef` (with Designer): drops the unique index on `CustomerGroups.BaleId`.
6. `20261116100000_MessageAttachmentsAndThreads` – `SupportThreads`, `CustomerGroupedMessages.ThreadId` / `OriginalText`,
   `MessageAttachments`. Existing messages keep NULL thread and text. SQL Server adds the column and the foreign key;
   the SQLite branch (tests) adds the column with an inline `REFERENCES`. `Down` is SQL Server only.

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

`dotnet test SiloSupport.slnx` — `UiConventionTests` guard the UI conventions (RTL document, bundled Vazirmatn files, drawer closed by default, sidebar links, role attribute on every admin page, no bot code in Web) by reading the Web source files; it covers priority parsing, the dashboard statistics (totals, per-customer, daily buckets in Iran time, window boundaries), Persian date/priority formatting, the text Bale id (validation, storage, matching a numeric sender), the prompt and formatter, all routing rules (fakes for
repositories, Bale and the AI API), the Bale and AI adapters (stub handlers, no network), EF constraints / repositories /
services (SQLite in-memory) including the conversation queries and the resolve flag, and the migration chain.
The Blazor Web project has no tests by decision.

## Known limitations

- Only `message` updates are handled (not `edited_message`). Stickers, contacts and locations are ignored. Voice
  messages are stored and forwarded, **not transcribed**, so the AI judges them only by their caption (a voice-only
  message is filed as Medium at least).
- Files bigger than `Bale:MaxDownloadMegabytes` (Bale's download limit is 20 MB) are forwarded by `file_id` and recorded
  without bytes; the panel says so. Files are stored in the database, so watch its size; set a smaller limit if needed.
- An album (several photos sent together, `media_group_id`) arrives as separate messages; the AI usually links them
  to one issue.
- Thread assignment is the AI's decision, so it can be wrong; the history is shown in the panel and each message keeps
  its own priority and group.
- The worker starts at offset 0 and acknowledges a batch on the next poll, so after a restart Bale may re-deliver the
  last batch it had not yet seen acknowledged (and anything older than that, up to 24 h, if the bot was offline). A
  message already forwarded just before a restart could therefore be forwarded twice.
- Messages are processed concurrently (max 5), so forwarded order across messages is not guaranteed.
- `SiloSupport.Service.CustomerChat/appsettings.json` contains what look like a real Bale bot token and AI API key.
  That project is read-only for this task; make sure the repository stays private (or rotate those credentials).
