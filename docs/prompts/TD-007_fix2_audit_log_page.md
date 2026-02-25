# TD-007 Fix 2: Implement Audit Log Page

## Context
The `AuditLog.razor` page is a stub placeholder. The backend is fully functional:
- `AuditEvent` entity with Action, PerformedBy, TargetResource, Success, etc.
- `IAuditEventRepository` with `GetRecentAsync()`, `SearchAsync()`, `GetByActionAsync()`, etc.
- `AuditEventEndpoints.cs` with full REST API at `/api/audit-events/`
- Events ARE being written by AccountController (login, password change) and all CRUD endpoints

The Blazor page just needs to query and display the data.

## File to Replace
`admin/dotnet/src/LucidAdmin.Web/Components/Pages/AuditLog.razor`

Current content is 18 lines of hardcoded placeholder text.

## Requirements

### Layout
Follow the MudBlazor pattern used by `Agents.razor` and `Settings/Users.razor`:
- Page title and subtitle
- Filter row with search and dropdowns
- MudTable with sortable columns

### Columns
| Column | Source | Notes |
|--------|--------|-------|
| Timestamp | `CreatedAt` | Formatted as relative time ("2 min ago") with tooltip showing full datetime |
| Action | `Action` (enum) | Colored MudChip: green for success operations, red for failures, blue for info |
| Performed By | `PerformedBy` | Username or "System" |
| Target | `TargetResource` | Username, group name, path, etc. |
| Status | `Success` | Green check / red X icon |
| Details | `ErrorMessage` or `DetailsJson` | Truncated, expandable on click |

### Filters
1. **Search text** — filters across PerformedBy, TargetResource, TicketNumber (client-side)
2. **Action type dropdown** — populated from `AuditAction` enum values
3. **Status dropdown** — Success / Failed / All
4. **Date range** — optional From/To date pickers (pass to `SearchAsync`)

### Data Loading
Since this is Blazor Server, inject `IAuditEventRepository` directly (no HTTP call needed).

```csharp
@inject IAuditEventRepository AuditRepository
```

On page load, call `GetRecentAsync(limit: 200)`. When filters change, use `SearchAsync()`.

### Action Chip Colors
Group the AuditAction enum values by category for coloring:
- **Auth events** (UserLogin, UserLogout, PasswordChanged): Color.Info (blue)
- **Create events** (*Created, *Registered): Color.Success (green)
- **Update events** (*Updated): Color.Warning (amber)
- **Delete events** (*Deleted, *Revoked, *Deregistered): Color.Error (red)
- **Failed events** (*Failed): Color.Error (red)
- **Other**: Color.Default (grey)

### Pagination
Use MudTable's built-in pagination: `RowsPerPage="25"` with page selector.

### Empty State
If no events match filters: "No audit events found matching your criteria."
If no events at all: "No audit events recorded yet. Events are logged automatically as you use the system."

## Reference Files
- Pattern to follow: `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Agents.razor` (MudTable + filters)
- Pattern to follow: `admin/dotnet/src/LucidAdmin.Web/Components/Pages/Settings/Users.razor` (badges + filtering)
- Repository interface: `admin/dotnet/src/LucidAdmin.Core/Interfaces/Repositories/IAuditEventRepository.cs`
- Entity: `admin/dotnet/src/LucidAdmin.Core/Entities/AuditEvent.cs`
- Enum: `admin/dotnet/src/LucidAdmin.Core/Enums/AuditAction.cs`

## Verification
1. `dotnet build` — no errors
2. Run portal, login as admin
3. Audit log page loads and shows recent events (at minimum: the login event)
4. Create a service account → verify ServiceAccountCreated appears
5. Search filter works
6. Action type filter works
7. Pagination works with 25+ events
