# API reference

Base URL for the included HTTP launch profile: `http://localhost:5239`. Protected operations require `Authorization: Bearer <JWT>`. JSON enum values use camel-case names such as `inProgress` and `critical`; integer enum input is disabled.

## Authentication and authorization

JWTs are validated for HMAC-SHA256 signature, issuer, audience, and expiration. Services use the validated `sub` and `role` claims; request bodies do not select the acting user. Public registration creates an active Employee account, not an Agent or Admin.

| Role | Ticket visibility | Additional permissions |
|---|---|---|
| Admin | All tickets | Assign/reassign/unassign; delete; status changes; active Agent directory; global dashboard. |
| Agent | Tickets assigned to that Agent | Status changes and comments/history for assigned tickets; own dashboard workload. |
| Employee | Tickets created by that Employee | Create/update own tickets; comments/history for own tickets; own-ticket dashboard metrics. |

The ticket visibility rules are applied before ticket search, optional query filters, counts, and pagination. A ticket not visible to a caller is generally returned as `404 Not Found` for single-ticket and participant operations.

## Authentication

### `POST /api/auth/register`

**Access:** Anonymous. Creates an Employee.  
**Request:**

```json
{
  "fullName": "Morgan Lee",
  "email": "morgan.lee@example.test",
  "password": "<locally-entered-password-meeting-policy>"
}
```

Password must be at least 12 characters, include uppercase and lowercase letters, a digit, and a symbol, and be no more than 72 UTF-8 bytes. The role is assigned as `Employee` by the service.

**Responses:** `201 Created` with an `AuthenticationResponse` (`userId`, safe account fields, role, access token, expiry); `400 Bad Request` for invalid model input; `409 Conflict` if the email is already registered. The current `Location` header points to `/api/auth/users/{id}`, but there is no user-profile GET action in this API.

### `POST /api/auth/login`

**Access:** Anonymous.

```json
{
  "email": "morgan.lee@example.test",
  "password": "<the-account-password>"
}
```

**Responses:** `200 OK` with `AuthenticationResponse`; `400 Bad Request` for invalid model input; `401 Unauthorized` for an invalid credential pair or an inactive account. Password hashes are never returned.

## Agent directory

### `GET /api/users/agents`

**Access:** Admin only.  
Returns active Agent accounts sorted by full name and then user ID. Response fields are `userId`, `fullName`, and `email`; password hashes and inactive/non-Agent accounts are excluded.

**Responses:** `200 OK`; `401 Unauthorized` without a valid token; `403 Forbidden` for a non-Admin.

## Tickets

### `POST /api/tickets`

**Access:** Any authenticated supported role. Creator identity comes from the JWT.

```json
{
  "title": "Cannot connect to VPN",
  "description": "The VPN client fails to connect from my workstation.",
  "categoryId": 4,
  "priority": "high"
}
```

The category must exist and be active. A new ticket starts in `open`; `CreatedAtUtc`, `UpdatedAtUtc`, `DueAtUtc`, creator identity, and initial history are assigned by the service. Assignment and status cannot be set in this request.

**Responses:** `201 Created` with `TicketResponse`; `400 Bad Request` for invalid fields/category/priority; `401 Unauthorized`; `403 Forbidden` if the validated identity is not a supported role.

### `GET /api/tickets`

**Access:** Any authenticated role; only visible tickets are considered.  
**Query parameters:**

| Parameter | Meaning and validation |
|---|---|
| `search` | Optional title or description substring; at most 200 characters. SQL Server collation determines case behavior. |
| `status` | Optional `open`, `inProgress`, `resolved`, or `closed`. |
| `priority` | Optional `low`, `medium`, `high`, or `critical`. |
| `categoryId` | Optional positive category ID. |
| `assignedToUserId` | Optional positive assigned user ID. |
| `createdByUserId` | Optional positive creator ID. |
| `isOverdue` | Optional `true` or `false`. |
| `sortBy` | `createdAtUtc`, `updatedAtUtc`, `dueAtUtc`, `priority`, or `status`. Default: `createdAtUtc`. |
| `sortDirection` | `asc` or `desc`. Default: `desc`. |
| `pageNumber` | Positive integer. Default: `1`. |
| `pageSize` | Integer from `1` through `100`. Default: `10`. |

Sorting adds ticket ID as a deterministic secondary key. Filtering, count, sorting, and `Skip`/`Take` remain in the EF query sent to SQL Server.

Example:

```text
GET /api/tickets?search=VPN&status=open&priority=high&categoryId=4&isOverdue=true&sortBy=updatedAtUtc&sortDirection=desc&pageNumber=1&pageSize=10
Authorization: Bearer <JWT>
```

**Response:** `200 OK` with `items` and pagination properties `pageNumber`, `pageSize`, `totalCount`, `totalPages`, `hasPreviousPage`, `hasNextPage`. `400 Bad Request` for unsupported sort fields/directions, invalid enum/filter values, or invalid page values; `401 Unauthorized` if unauthenticated.

### `GET /api/tickets/overdue`

**Access:** Any authenticated role; returns only overdue tickets visible to the caller.  
**Responses:** `200 OK` with an array of `TicketResponse` values (possibly empty); `401 Unauthorized`.

### `GET /api/tickets/{id}`

**Access:** Any authenticated role, restricted to visible tickets.  
**Responses:** `200 OK` with `TicketResponse`; `401 Unauthorized`; `404 Not Found` if missing or not visible.

### `PUT /api/tickets/{id}`

**Access:** Any authenticated role, restricted to visible tickets. This updates title, description, category, and priority only; status and assignment have dedicated operations.

```json
{
  "title": "VPN connection failure",
  "description": "The VPN client still fails to connect.",
  "categoryId": 4,
  "priority": "high"
}
```

A priority change recalculates `DueAtUtc`; changing unrelated fields does not. Meaningful updates add a history event.

**Responses:** `200 OK` with `TicketResponse`; `400 Bad Request` for invalid fields or inactive/missing category; `401 Unauthorized`; `403 Forbidden` for invalid identity; `404 Not Found` if missing or not visible.

### `PUT /api/tickets/{id}/assignment`

**Access:** Admin only. Assigns/reassigns to an active Agent, or unassigns when `agentUserId` is explicitly `null`. Actor identity is read from the JWT. Repeating the current assignment succeeds without adding another history event.

Assign or reassign:

```json
{
  "agentUserId": 2
}
```

Unassign:

```json
{
  "agentUserId": null
}
```

**Response:** `200 OK` with `ticketId`, `ticketTitle`, `assignedAgentUserId`, and `assignedAgentName`. No assignment timestamp is included because the model has no assignment timestamp field. `400 Bad Request` for non-Agent or inactive target accounts; `401 Unauthorized`; `403 Forbidden` for non-Admins; `404 Not Found` for a missing ticket or target Agent.

### `PUT /api/tickets/{id}/status`

**Access:** Admin or the Agent assigned to the ticket. Employees cannot call this operation; an Agent cannot change an unassigned or another Agent's ticket.

```json
{
  "status": "inProgress"
}
```

Allowed transitions are `open -> inProgress`, `inProgress -> resolved`, `resolved -> inProgress`, and `resolved -> closed`. Other transitions return `400 Bad Request`. A successful transition updates `UpdatedAtUtc` and records old/new status and actor in ticket history.

**Responses:** `200 OK` with `TicketResponse`; `400 Bad Request` for an invalid status or transition; `401 Unauthorized`; `403 Forbidden` for Employee; `404 Not Found` if the ticket does not exist or is not assigned to the Agent.

### `DELETE /api/tickets/{id}`

**Access:** Admin only.  
**Responses:** `204 No Content` when deleted; `401 Unauthorized`; `403 Forbidden`; `404 Not Found`; `409 Conflict` when restricted related comments/history prevent deletion.

## Comments and audit history

For these routes, the ticket creator, assigned Agent, and Admin can access the content. Other Employees and Agents who are not assigned receive `404 Not Found`, as do missing tickets. Actor identity and UTC timestamps are set by the service.

### `POST /api/tickets/{id}/comments`

```json
{
  "commentText": "The troubleshooting steps are underway."
}
```

Comment text must contain non-whitespace characters and be at most 4000 characters. The response includes comment ID, ticket ID, safe author ID/name, text, and UTC creation time.

**Responses:** `201 Created`; `400 Bad Request` for empty/whitespace or oversized text; `401 Unauthorized`; `404 Not Found` if missing or not accessible.

### `GET /api/tickets/{id}/comments`

Returns an array of `TicketCommentResponse` ordered by creation time and ID.  
**Responses:** `200 OK`; `401 Unauthorized`; `404 Not Found` if missing or not accessible.

### `GET /api/tickets/{id}/history`

Returns an array of `TicketHistoryResponse` ordered by change time and ID. History records include actor identity, action, optional old/new status, and UTC timestamp. Ticket creation, assignment changes, status changes, and meaningful ticket updates are recorded.

**Responses:** `200 OK`; `401 Unauthorized`; `404 Not Found` if missing or not accessible.

## Dashboard

### `GET /api/dashboard/summary`

**Access:** Any authenticated role.  
Returns total ticket count, counts for every status and priority, overdue ticket count, and Agent workload entries (`agentUserId`, `agentName`, `activeAssignedTicketCount`, `overdueAssignedTicketCount`).

- Admin: global ticket metrics and every active Agent's workload.
- Agent: only tickets assigned to the caller and the caller's workload.
- Employee: only tickets created by the caller and no Agent workload data.

**Responses:** `200 OK`; `401 Unauthorized`; `403 Forbidden` if the service cannot derive a supported role and user ID from validated claims.

## Health and diagnostics

| Method and route | Access | Behavior and responses |
|---|---|---|
| `GET /health/live` | Anonymous | Process liveness only; `200 OK` when the API process responds. |
| `GET /health/ready` | Anonymous | Includes SQL Server connectivity; `200 OK` when healthy, `503 Service Unavailable` when unhealthy. Response body contains status only. |

Unexpected exceptions are logged server-side and returned as generic `application/problem+json` with HTTP 500. Internal exception text, SQL details, and stack traces are not returned. If the client cancels a request, the exception handler does not generate a 500 response.

## Development-only endpoints

- `GET /openapi/v1.json` is mapped in Development.
- `GET /weatherforecast` is the retained ASP.NET Core template sample, not a HelpDeskPro business endpoint.
