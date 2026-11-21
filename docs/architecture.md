# Architecture and workflows

These diagrams describe the current HelpDeskPro implementation. The API is a single ASP.NET Core application; the diagram does not imply separately deployed services.

## 1. High-level system architecture

```mermaid
flowchart LR
    Client["API client"]
    App["ASP.NET Core Web API"]
    Auth["JWT authentication and role policies"]
    Controllers["Controllers"]
    Services["Application services"]
    EF["ApplicationDbContext and EF Core"]
    SQL[("SQL Server 2022")]
    Clock["TimeProvider and SLA service"]
    Ops["ProblemDetails, logging, health checks"]

    Client -->|HTTP / bearer JWT| App
    App --> Auth
    Auth --> Controllers
    Controllers --> Services
    Services --> EF
    EF --> SQL
    Services --> Clock
    App --> Ops
    Ops -->|readiness connectivity check| SQL
```

## 2. Layered application architecture

```mermaid
flowchart TB
    Http["HTTP requests and responses"]
    Controller["Controllers<br/>route, bind, authorize, translate results"]
    Contracts["DTOs and service interfaces"]
    Service["Services<br/>business rules, visibility, queries"]
    Persistence["ApplicationDbContext<br/>entity configurations and migrations"]
    Domain["Models and enums"]
    Database[("SQL Server")]
    CrossCutting["JWT, authorization, exception handling,<br/>logging, health checks"]

    Http --> Controller
    Controller --> Contracts
    Controller --> Service
    Service --> Contracts
    Service --> Persistence
    Persistence --> Domain
    Persistence --> Database
    CrossCutting -. surrounds .-> Controller
```

## 3. Authentication and authorization flow

```mermaid
sequenceDiagram
    actor User
    participant API as AuthController / AuthService
    participant DB as SQL Server
    participant Client as API client
    participant Bearer as JWT bearer middleware
    participant Endpoint as Protected endpoint

    User->>API: Register or submit login credentials
    API->>DB: Find user and persist new Employee when registering
    API->>API: Verify or hash password with BCrypt
    API-->>Client: Return signed JWT and safe user fields
    Client->>Bearer: Send Authorization: Bearer token
    Bearer->>Bearer: Validate signature, issuer, audience, lifetime
    Bearer->>Endpoint: Set authenticated sub and role claims
    Endpoint->>Endpoint: Apply role policy and ticket ownership/assignment scope
    Endpoint-->>Client: Return authorized DTO or standard error
```

Registration creates Employee accounts only. JWTs use HMAC-SHA256 and are validated for signing key, issuer, audience, and lifetime before protected actions run.

## 4. Ticket creation and assignment flow

```mermaid
flowchart TD
    Create["Authenticated caller submits ticket fields"]
    Validate["Validate claims, text, priority, and active category"]
    Init["Set creator from JWT; status Open; UTC timestamps"]
    Deadline["Calculate DueAtUtc from priority SLA"]
    History["Record Ticket created event"]
    Save["Save ticket and history"]
    Admin["Admin requests assignment change"]
    AgentCheck["Validate ticket and active Agent account"]
    Assign["Set AssignedToUserId, or null to unassign"]
    AssignmentHistory["Record assigned, reassigned, or unassigned event"]

    Create --> Validate --> Init --> Deadline --> History --> Save
    Admin --> AgentCheck --> Assign --> AssignmentHistory --> Save
```

Assignment is Admin-only. Assignment history is created only when the assigned Agent actually changes.

## 5. Ticket status workflow

```mermaid
stateDiagram-v2
    [*] --> Open
    Open --> InProgress
    InProgress --> Resolved
    Resolved --> InProgress: Reopen
    Resolved --> Closed
```

Other transitions are rejected. Admins may update any ticket; an Agent may update only a ticket assigned to that Agent. A successful transition updates `UpdatedAtUtc` and adds a `TicketHistory` record. Employees cannot change status.

## 6. SLA deadline and overdue calculation

```mermaid
flowchart TD
    Created["Ticket created at current UTC time"]
    Priority{"Priority"}
    Low["Low: +72 calendar hours"]
    Medium["Medium: +48 calendar hours"]
    High["High: +24 calendar hours"]
    Critical["Critical: +4 calendar hours"]
    Due["Persist DueAtUtc"]
    Check["Compare current UTC time to DueAtUtc"]
    Closed{"Status Resolved or Closed?"}
    Overdue{"Current time > DueAtUtc?"}
    Result["IsOverdue = true"]
    NotOverdue["IsOverdue = false"]
    Reopen["Reopen Resolved ticket"]

    Created --> Priority
    Priority -->|Low| Low
    Priority -->|Medium| Medium
    Priority -->|High| High
    Priority -->|Critical| Critical
    Low --> Due
    Medium --> Due
    High --> Due
    Critical --> Due
    Due --> Check --> Closed
    Closed -->|Yes| NotOverdue
    Closed -->|No| Overdue
    Overdue -->|Yes| Result
    Overdue -->|No, including exact deadline| NotOverdue
    Reopen --> Check
```

Priority changes recalculate the deadline. Edits to unrelated fields and reopening a ticket do not reset it. All comparisons use UTC; the deadline is exceeded strictly after the exact due time.

## 7. Dashboard request flow

```mermaid
flowchart TD
    Request["GET /api/dashboard/summary"]
    Auth["Validate JWT and authenticated-user policy"]
    Role{"Role from validated claims"}
    Admin["Admin: all tickets; all active Agents"]
    Agent["Agent: assigned tickets; own workload only"]
    Employee["Employee: created tickets; no Agent workload"]
    Aggregates["EF Core count and grouped status/priority queries"]
    SLA["Reuse SLA overdue query"]
    Workload["Group active and overdue assignment counts"]
    DTO["Return role-scoped DashboardSummaryResponse"]

    Request --> Auth --> Role
    Role -->|Admin| Admin
    Role -->|Agent| Agent
    Role -->|Employee| Employee
    Admin --> Aggregates
    Agent --> Aggregates
    Employee --> Aggregates
    Aggregates --> SLA --> Workload --> DTO
```

Dashboard ticket totals and status/priority counts are aggregated in EF Core queries. Admin workloads include active Agent accounts; Agent workloads include only the caller; Employee responses contain no Agent workload records.
