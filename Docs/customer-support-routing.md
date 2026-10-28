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
| `SiloSupport.Application` | net9.0 | Ports (`I*Repository`, `IBaleBotClient`, `IMessagePriorityClassifier`, `ITransactionRunner`), services, DTOs, pure helpers |
| `SiloSupport.Infrastructure` | net9.0 | `BaleBotApiClient`, `SiloAiPriorityClassifier`, `BaleBotOptions` (+ the existing `SiloAiClient`) |
| `SiloSupport.Persistence` | net10.0 | `ApplicationDbContext` (Identity + domain), EF configurations, repositories, `EfTransactionRunner`, migrations, `AddSupportPersistence` |
| `SiloSupport.Service.CustomerSupport` | net10.0 | **New Windows service**: Bale long-polling worker, composition of the Bale/AI adapters and the message processor |
| `SiloSupport.Web` | net10.0 | Blazor Web App (Interactive Server + **Telerik UI for Blazor**), Identity login, admin pages. References Application + Persistence only |
| `SiloSupport.Tests` | net10.0 | xUnit + SQLite in-memory for Application / Persistence / Infrastructure. **No Web tests** |

`SiloSupport.Service.CustomerChat` is **unchanged**. Domain/Application/Infrastructure stay on net9.0 because that
service (net9.0) references Application and Infrastructure; a net9.0 project cannot reference net10.0 projects.
`SiloSupport.Application.csproj` gained a Domain reference and a `Microsoft.Extensions.Logging.Abstractions` reference;
no source file of the existing service changed. The new service is net10.0 because it uses Persistence (EF Core 10).

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

- `CustomerMember.BaleId`, `CustomerGroup.BaleId`: `long` (Bale ids can exceed 32 bits, up to 52 significant bits).
- Unique: `CustomerMembers.BaleId`; `CustomerGroups.BaleId`; `CustomerGroups(CustomerId, Priority)`.
- `CustomerGroups.Priority` is stored as text (`Low`/`Medium`/`Critical`).
- `CustomerGroupedMessages.GroupId → CustomerGroups` is `Restrict`: groups and customers with history cannot be
  deleted (the services check first and show a message; the database enforces it). Members and groups without
  history are removed together with their customer.
- Business uniqueness is enforced by the services (friendly messages) **and** by the database.

## Admin UI (`SiloSupport.Web`, Telerik UI for Blazor, `[Authorize(Roles = "Administrator")]`)

| Route | Page |
|---|---|
| `/admin/customers` | list (member count, missing priority groups), delete |
| `/admin/customers/new`, `/{id}/edit` | create / edit |
| `/admin/customers/{id}` | details: members, the three priority slots, missing-group warning |
| `/admin/customers/{id}/members/new`, `/{memberId}/edit` | member form (customer shown explicitly) |
| `/admin/customers/{id}/groups/new`, `/{groupId}/edit` | destination group form |
| `/admin/conversations[/{customerId}]` | forwarded messages of one customer, per group, oldest first, resolve switch |

The pages only use Application services. The conversations page loads one customer's messages with a database filter.
Telerik components used: `TelerikRootComponent`, `TelerikGrid`, `TelerikTextBox`, `TelerikTextArea`,
`TelerikNumericTextBox`, `TelerikDropDownList`, `TelerikSwitch`, `TelerikButton`, `DialogFactory` (confirmations).

Telerik UI for Blazor is a commercial product: `Telerik.UI.for.Blazor` (version pinned in `Directory.Packages.props`)
must be restorable from your Telerik feed / local package folder in `NuGet.Config`, and a Telerik license key must be
installed on the build machine (see Telerik's licensing documentation), otherwise the build shows a license warning.

## Configuration

Placeholders only in the `appsettings.json` files; supply real values with user secrets or environment variables.

**Windows service** (`SiloSupport.Service.CustomerSupport`):

```
Database__ConnectionString=...
Bale__BotToken=...                       # from @botfather
SiloAI__BaseUrl=...  SiloAI__ApiKey=...
```

**Web** (`SiloSupport.Web`):

```
Database__ConnectionString=...           # same database as the service
AdminSeed__UserName=...  AdminSeed__Password=...   # first administrator, created at startup if missing
```

`Bale:Enabled=false` stops the polling worker; it also does not start when no real token is configured. The Bale bot
token is part of the Bale request URL, so `System.Net.Http.HttpClient` logging is kept at `Warning` (already set) to
avoid writing it to logs. The service logs with Serilog to the console and to `logs/customer-support-.log` next to the
executable.

Group chat ids: add the bot to each destination group; the chat id is the numeric `chat.id` Bale reports (usually
negative for groups).

### Installing the Windows service

```
dotnet publish SiloSupport.Service.CustomerSupport -c Release -o C:\Services\SiloCustomerSupport
sc.exe create "Silo Customer Support Service" binPath= "C:\Services\SiloCustomerSupport\SiloSupport.Service.CustomerSupport.exe" start= auto
```

Set the secrets as machine-level environment variables (or in an `appsettings.Production.json` kept out of source
control) before starting it.

## Migrations

The **Web project owns and applies the migrations** at startup (`Database.MigrateAsync()`); the Windows service never
migrates, so start the Web site (or run `dotnet ef database update`) before the service.

`SiloSupport.Persistence/Migrations/20261027090000_InitialCreate` (Identity + support tables) was written by hand together
with its model snapshot because it was authored in an environment without the .NET SDK. Before relying on it, from the
solution root run:

```
dotnet tool restore --tool-manifest SiloSupport.Web/.config/dotnet-tools.json
dotnet ef migrations has-pending-model-changes -p SiloSupport.Persistence -s SiloSupport.Web
```

If it reports differences (for example the Identity schema version), delete the two migration files and regenerate:

```
dotnet ef migrations add InitialCreate -p SiloSupport.Persistence -s SiloSupport.Web
```

`MigrationTests` applies the chain to SQLite.

## Tests

`dotnet test SiloSupport.slnx` — covers priority parsing, the prompt and formatter, all routing rules (fakes for
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
  That project is read-only for this task; rotate those credentials and move them to secrets.
