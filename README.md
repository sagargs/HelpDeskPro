# HelpDeskPro

HelpDeskPro is a backend-only IT support and incident-management API. It gives employees a place to report IT issues, lets administrators assign work to active Agents, and provides controlled status changes, comments, audit history, and SLA visibility.

## Business problem and objectives

IT support requests can be difficult to route, track, and review when they are handled through separate messages and spreadsheets. HelpDeskPro provides a single API-backed workflow for:

- Creating and tracking categorized support tickets.
- Restricting ticket access according to the authenticated user's role and relationship to each ticket.
- Assigning tickets to active Agents and recording ticket events.
- Applying priority-based calendar-hour deadlines and identifying overdue work.
- Providing paginated ticket search and role-scoped operational dashboard metrics.

## Implemented features

- Employee registration and JWT login; passwords are stored as BCrypt hashes.
- Role-aware access for Admin, Agent, and Employee accounts.
- Ticket creation, scoped listing, reading, editing, and Admin-only deletion.
- Admin-only Agent directory, ticket assignment, reassignment, and unassignment.
- Controlled ticket status transitions, comments, and ticket history.
- Calendar-hour SLA deadlines, overdue ticket queries, and `IsOverdue` response fields.
- SQL Server-backed search, filters, sorting, and pagination.
- Role-scoped dashboard summary and Agent workloads.
- Centralized ProblemDetails handling, structured application lifecycle/error logs, and liveness/readiness probes.

The API does not include a frontend, email notifications, background processing, business-hour calendars, or deployment automation.

## Technology

| Area | Technology |
|---|---|
| Runtime and API | C#, ASP.NET Core Web API, .NET 10 |
| Database | SQL Server 2022 |
| Data access | Entity Framework Core 10.0.15 |
| Authentication | JWT bearer tokens; BCrypt.Net-Next 4.0.3 |
| Tests | xUnit 2.9.3, EF Core InMemory 10.0.15 |
| API description | ASP.NET Core OpenAPI |
| CI | GitHub Actions |

## Architecture

The API is a layered modular monolith: controllers handle HTTP, services contain application rules and data access, DTOs define API payloads, and EF Core configurations map entities to SQL Server. Cross-cutting authentication, authorization, error handling, logging, SLA time, and health checks are configured at the API boundary.

See [architecture diagrams](docs/architecture.md) and [database documentation](docs/database.md).

## Repository structure

```text
.
├── .github/workflows/ci.yml
├── docs/
│   ├── api-reference.md
│   ├── architecture.md
│   └── database.md
├── src/HelpDeskPro.API/
│   ├── Configuration/       # JWT and authorization policy configuration
│   ├── Controllers/         # HTTP endpoints
│   ├── Data/                # DbContext, EF configurations, migrations
│   ├── DTOs/                # Request and response contracts
│   ├── Enums/               # Roles, ticket priorities, ticket statuses
│   ├── Extensions/          # Dependency injection and OpenAPI helpers
│   ├── Interfaces/          # Service contracts
│   ├── Middleware/          # Exception and health-check components
│   ├── Models/              # EF Core entities
│   └── Services/            # Authentication, tickets, SLA, dashboard
├── tests/HelpDeskPro.Tests/  # xUnit tests
└── HelpDeskPro.slnx
```

The existing WeatherForecast controller and request sample are retained as ASP.NET Core template scaffolding; they are not HelpDeskPro support features.

## Prerequisites

- Windows with PowerShell.
- .NET 10 SDK.
- SQL Server 2022 Developer Edition running locally (or an accessible SQL Server instance).
- Git.
- Optional: SQL Server Management Studio and Visual Studio Code.

The project targets `net10.0`. EF Core runtime, SQL Server, and design packages are version `10.0.15`.

## Local setup (Windows / PowerShell)

Clone the repository and enter its root:

```powershell
git clone https://github.com/sagargs/HelpDeskPro.git
Set-Location .\HelpDeskPro
dotnet --version
```

The installed SDK should report a .NET 10 version.

### SQL Server configuration

Development configuration in `src/HelpDeskPro.API/appsettings.Development.json` uses Windows integrated authentication:

```text
Server=localhost;Database=HelpDeskProDb;Trusted_Connection=True;TrustServerCertificate=True;
```

Start the local SQL Server service before applying migrations. To use another instance, override `ConnectionStrings:DefaultConnection` through User Secrets or the environment variable `ConnectionStrings__DefaultConnection`; do not commit credentials:

```powershell
$apiProject = ".\src\HelpDeskPro.API\HelpDeskPro.API.csproj"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<local-sql-server-connection-string>" --project $apiProject
```

### JWT signing key with User Secrets

The application requires a signing key of at least 32 UTF-8 bytes at startup. The repository intentionally does not contain one. The API project already has a `UserSecretsId`; store a locally generated, unique value:

```powershell
$apiProject = ".\src\HelpDeskPro.API\HelpDeskPro.API.csproj"
dotnet user-secrets set "Jwt:SigningKey" "<locally-generated-secret-at-least-32-UTF8-bytes>" --project $apiProject
```

Replace the placeholder locally; never put the actual key in source control, documentation, a request example, or a terminal transcript you intend to share. User Secrets are for local development, not a production secret store. The configured issuer, audience, and default token lifetime are in `appsettings.json`; the lifetime must be between 1 and 1440 minutes.

### Apply the existing EF Core migrations

Install or update the matching EF command-line tool:

```powershell
if (Get-Command dotnet-ef -ErrorAction SilentlyContinue) {
    dotnet tool update --global dotnet-ef --version 10.0.15
}
else {
    dotnet tool install --global dotnet-ef --version 10.0.15
}
```

Apply the checked-in migrations to create/update the local database:

```powershell
$apiProject = ".\src\HelpDeskPro.API\HelpDeskPro.API.csproj"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef database update --project $apiProject --startup-project $apiProject
```

The repository contains `InitialCreate` and `SeedDefaultCategories` migrations. To create a future migration, use a descriptive name and keep it under `Data\Migrations`:

```powershell
$apiProject = ".\src\HelpDeskPro.API\HelpDeskPro.API.csproj"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef migrations add DescriptiveMigrationName --project $apiProject --startup-project $apiProject --output-dir Data\Migrations
```

Keep `ASPNETCORE_ENVIRONMENT` set to `Development` for these commands so the startup project loads its local connection string and User Secrets. The API does not automatically apply migrations on startup. Review generated migrations and back up any non-development database before applying schema changes.

### Run the API

```powershell
dotnet run --project .\src\HelpDeskPro.API\HelpDeskPro.API.csproj --launch-profile http
```

The HTTP launch profile listens at `http://localhost:5239`. With the HTTPS profile, the configured addresses are `https://localhost:7195` and `http://localhost:5239`. The built-in OpenAPI document is mapped in Development at `/openapi/v1.json`.

Public registration creates Employee accounts only. Provisioning Admin and Agent accounts is not exposed as a public API operation and must be handled through an authorized administrative process.

### Run tests

The test project uses xUnit and EF Core InMemory. SQL Server-provider model checks inspect metadata without opening a connection, so the automated suite does not require a live SQL Server.

```powershell
dotnet restore .\HelpDeskPro.slnx
dotnet build .\HelpDeskPro.slnx --configuration Release
dotnet test .\HelpDeskPro.slnx --configuration Release
```

## API endpoint reference

All ticket endpoints require a valid bearer JWT. Visibility is enforced in addition to endpoint-level role policies: Admins see all tickets, Employees see tickets they created, and Agents see tickets assigned to them.

| Method | Route | Authentication / permission | Purpose |
|---|---|---|---|
| `POST` | `/api/auth/register` | Anonymous | Register an Employee |
| `POST` | `/api/auth/login` | Anonymous | Authenticate and receive a JWT |
| `GET` | `/api/users/agents` | Admin | List active Agents using safe fields |
| `POST` | `/api/tickets` | Authenticated | Create a ticket |
| `GET` | `/api/tickets` | Authenticated | Search, filter, sort, and page visible tickets |
| `GET` | `/api/tickets/overdue` | Authenticated | List overdue tickets within the caller's visibility |
| `GET` | `/api/tickets/{id}` | Authenticated | Read a visible ticket |
| `PUT` | `/api/tickets/{id}` | Authenticated | Edit ticket title, description, category, or priority |
| `PUT` | `/api/tickets/{id}/status` | Admin or assigned Agent | Apply an allowed status transition |
| `PUT` | `/api/tickets/{id}/assignment` | Admin | Assign, reassign, or unassign an Agent |
| `POST` | `/api/tickets/{id}/comments` | Ticket creator, assigned Agent, or Admin | Add a comment |
| `GET` | `/api/tickets/{id}/comments` | Ticket creator, assigned Agent, or Admin | Read ticket comments |
| `GET` | `/api/tickets/{id}/history` | Ticket creator, assigned Agent, or Admin | Read ticket audit history |
| `DELETE` | `/api/tickets/{id}` | Admin | Delete a ticket if no restricted related rows prevent it |
| `GET` | `/api/dashboard/summary` | Authenticated | Read role-scoped ticket metrics |
| `GET` | `/health/live` | Anonymous | Check API process liveness |
| `GET` | `/health/ready` | Anonymous | Check API and SQL Server readiness |

The retained `GET /weatherforecast` route is framework-template scaffolding. Detailed request bodies, response behavior, and common status codes are in the [API reference](docs/api-reference.md).

## Roles, workflow, and SLA

JWTs carry the user ID (`sub`) and role. Registration always assigns `Employee`; only Admins can use the Agent directory and assignment endpoint. Ticket status changes are limited to Admins and the Agent assigned to that ticket.

Allowed transitions:

```text
Open -> InProgress -> Resolved -> Closed
                         ^ 
                         └── InProgress (reopen)
```

SLA deadlines use calendar hours from the current UTC time: Low 72 hours, Medium 48, High 24, Critical 4. A ticket is overdue only when current UTC time is strictly later than its deadline and its status is neither Resolved nor Closed. Changing priority recalculates the deadline; unrelated edits do not. Reopening a Resolved ticket retains its existing deadline.

## Ticket search, filtering, sorting, and pagination

`GET /api/tickets` accepts `search`, `status`, `priority`, `categoryId`, `assignedToUserId`, `createdByUserId`, and `isOverdue`. Sorting uses `sortBy` (`createdAtUtc`, `updatedAtUtc`, `dueAtUtc`, `priority`, or `status`) and `sortDirection` (`asc` or `desc`). The default is `createdAtUtc` descending with ticket ID as a deterministic secondary key. Pagination defaults to `pageNumber=1&pageSize=10`; page size is limited to 100. Query parameters narrow the caller's already-authorized ticket set; they cannot expand it.

Example:

```text
GET http://localhost:5239/api/tickets?search=VPN&status=open&priority=high&categoryId=4&isOverdue=false&sortBy=updatedAtUtc&sortDirection=desc&pageNumber=1&pageSize=10
Authorization: Bearer <JWT>
```

The response includes `items`, `pageNumber`, `pageSize`, `totalCount`, `totalPages`, `hasPreviousPage`, and `hasNextPage`. Filtering, counting, sorting, and pagination are composed in EF Core before execution against SQL Server.

## Dashboard and health checks

- Admin dashboard metrics cover all tickets and active Agent workloads.
- Agent metrics cover only assigned tickets and that Agent's own workload.
- Employee metrics cover only tickets they created; Agent workload details are omitted.
- Health responses expose status only. Readiness includes a SQL Server connectivity check; neither probe requires a JWT.

## GitHub Actions

`.github/workflows/ci.yml` runs for pushes and pull requests targeting `master`. It sets up .NET 10, restores, builds Release, runs the xUnit suite, and uploads a TRX test-results artifact. The workflow does not deploy the application or require SQL Server credentials.

## Current limitations

- This repository contains an API and tests, not a user interface.
- Public registration creates Employee accounts only; Agent/Admin account provisioning is external to this API.
- There is no general user-profile read endpoint; the registration `Location` header points to `/api/auth/users/{id}`, for which this API currently has no GET action.
- SLA uses calendar hours only; there are no business calendars, notifications, or background SLA jobs.
- Health readiness checks SQL connectivity; it does not verify external services because none are configured.
- The test suite is SQL Server-independent; no live SQL Server integration test is included.
- CI validates restore/build/tests only; there is no deployment workflow.

## Further documentation

- [Architecture diagrams and request flows](docs/architecture.md)
- [Database entities, relationships, indexes, and constraints](docs/database.md)
- [Detailed API reference and request examples](docs/api-reference.md)
