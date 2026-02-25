# TD-007 Fix 3: Role-Based Access Control (RBAC) Enforcement

## Context
The Admin Portal has a complete RBAC infrastructure that is not being enforced:
- `AuthorizationPolicies.cs` defines `RequireAdmin`, `RequireOperator`, `RequireViewer`
- `Permissions.cs` and `RolePermissions.cs` map granular permissions per role
- `PermissionAuthorizationHandler.cs` validates permission claims
- `UserRole` enum: Admin, Operator, Viewer, Agent, ToolServer

**Problem**: Every page uses `@attribute [Authorize]` (any authenticated user). Every role
has identical access. The infrastructure exists but is never applied.

## Role Definitions

| Capability | Admin | Operator | Viewer |
|---|---|---|---|
| View dashboard, status, audit log | ✅ | ✅ | ✅ |
| View all configuration pages | ✅ | ✅ | ✅ |
| Run manual test workflows | ✅ | ✅ | ❌ |
| Approve/Reject pending approvals | ✅ | ✅ | ❌ |
| Create/Edit/Delete service accounts | ✅ | ❌ | ❌ |
| Create/Edit/Delete tool servers | ✅ | ❌ | ❌ |
| Create/Edit/Delete agents | ✅ | ❌ | ❌ |
| Create/Edit/Delete workflows | ✅ | ❌ | ❌ |
| Create/Edit/Delete rulesets | ✅ | ❌ | ❌ |
| Create/Edit/Delete examples | ✅ | ❌ | ❌ |
| Create/Edit/Delete capability mappings | ✅ | ❌ | ❌ |
| Create/Revoke API keys | ✅ | ❌ | ❌ |
| Manage users (Settings/Users) | ✅ | ❌ | ❌ |
| View AD settings | ✅ | ✅ | ❌ |
| Change own password | ✅ | ✅ | ✅ |

## Implementation Strategy

There are three enforcement layers needed:

### Layer 1: Page-Level Authorization (Blazor `@attribute`)

Dedicated create/edit pages should be restricted at the page level so lower-role users
who navigate directly to the URL get redirected to `/access-denied`.

**Admin-only pages** — change `@attribute [Authorize]` to `@attribute [Authorize(Policy = "RequireAdmin")]`:
- `ServiceAccounts/Create.razor`
- `ServiceAccounts/Edit.razor`
- `Rulesets/Edit.razor` (also handles /rulesets/create)
- `Examples/Edit.razor` (also handles /examples/create)
- `Workflows/Edit.razor` (also handles /workflows/create)
- `Workflows/Designer.razor`
- `Settings/Users.razor`

**Operator+ pages** — change to `@attribute [Authorize(Policy = "RequireOperator")]`:
- `Approvals/Index.razor`
- `ManualSubmissions.razor`
- `Settings/ActiveDirectory.razor`

**All authenticated users** — keep as `@attribute [Authorize]` (equivalent to RequireViewer):
- `Home.razor` (dashboard)
- `AuditLog.razor`
- `ChangePassword.razor`
- All Index/list pages (ServiceAccounts/Index, Agents, ToolServers, etc.)

NOTE: The `AuthorizationPolicies` class already needs to be imported. Add this using
at the top of each page that uses a policy:
```razor
@using Microsoft.AspNetCore.Authorization
```
The `[Authorize]` attribute without a policy already works because of the global using.
For policy-based auth, it's the same attribute: `[Authorize(Policy = "RequireAdmin")]`.

### Layer 2: Conditional UI Elements (Hide Buttons by Role)

For list pages that are accessible to all roles, mutation buttons (Add, Edit, Delete)
should only appear for users with the right role.

**Pattern**: Each page that needs role-aware UI should:
1. Inject `AuthenticationStateProvider`
2. In `OnInitializedAsync`, resolve the current user's role
3. Use `_isAdmin` / `_isOperatorOrAbove` booleans to conditionally render

Add this to the `@code` block of each affected list page:

```csharp
[CascadingParameter]
private Task<AuthenticationState>? AuthState { get; set; }

private bool _isAdmin;
private bool _isOperatorOrAbove;

protected override async Task OnInitializedAsync()
{
    if (AuthState != null)
    {
        var state = await AuthState;
        var user = state.User;
        _isAdmin = user.IsInRole("Admin");
        _isOperatorOrAbove = user.IsInRole("Admin") || user.IsInRole("Operator");
    }
    // ... existing load logic
}
```

Then wrap action buttons:

```razor
@if (_isAdmin)
{
    <MudButton Color="Color.Primary" OnClick="OpenCreateDialog">Add</MudButton>
}
```

**Pages that need conditional UI buttons:**

| Page | What to hide for non-Admin | What to hide for Viewer |
|---|---|---|
| `ServiceAccounts/Index.razor` | Add, Edit, Delete buttons | Same |
| `ToolServers.razor` | Add, Edit, Delete buttons | Same |
| `Agents.razor` | Add, Edit, Delete buttons | Same |
| `Rulesets/Index.razor` | Add, Edit, Delete buttons | Same |
| `Examples/Index.razor` | Add, Edit, Delete buttons | Same |
| `Workflows/Index.razor` | Add, Edit, Delete, Design buttons | Same |
| `CapabilityMappings.razor` | Add, Edit, Delete buttons | Same |
| `ApiKeys/Index.razor` | Create, Revoke, Delete buttons | Same |
| `Approvals/Index.razor` | (none — operators can approve) | Approve/Reject buttons |
| `ManualSubmissions.razor` | (none — operators can run) | (page blocked at Layer 1) |

For each page, search for `MudButton` or `MudIconButton` elements that trigger
Create, Edit, Delete, or similar mutation actions and wrap them in `@if (_isAdmin)`.

For the Approvals page specifically, keep Approve/Reject visible for Operator but not Viewer
(though Viewer can't access at all per Layer 1, so this is defense-in-depth).

### Layer 3: NavMenu Role Awareness

Update `Components/Layout/NavMenu.razor` to conditionally show menu items based on role.

The NavMenu needs the same CascadingParameter pattern:

```razor
@using Microsoft.AspNetCore.Components.Authorization

<MudNavMenu>
    @* Always visible *@
    <MudNavLink Href="/" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Dashboard">
        Dashboard
    </MudNavLink>

    @* View pages — always visible *@
    <MudNavLink Href="/service-accounts" ...>Service Accounts</MudNavLink>
    <MudNavLink Href="/tool-servers" ...>Tool Servers</MudNavLink>
    <MudNavLink Href="/agents" ...>Agents</MudNavLink>
    <MudNavLink Href="/rulesets" ...>Rulesets</MudNavLink>
    <MudNavLink Href="/examples" ...>Examples</MudNavLink>
    <MudNavLink Href="/workflows" ...>Workflows</MudNavLink>
    <MudNavLink Href="/capability-mappings" ...>Capability Mappings</MudNavLink>
    <MudNavLink Href="/audit-log" ...>Audit Log</MudNavLink>

    @* Operator+ only *@
    @if (_isOperatorOrAbove)
    {
        <MudNavLink Href="/approvals" ...>Approvals</MudNavLink>
        <MudNavLink Href="/manual-submissions" ...>Test Workflows</MudNavLink>
    }

    @* Admin only *@
    @if (_isAdmin)
    {
        <MudNavLink Href="/api-keys" ...>API Keys</MudNavLink>
    }

    <MudDivider Class="my-2" />
    <MudNavGroup Title="Settings" ...>
        @if (_isAdmin)
        {
            <MudNavLink Href="/settings/users" ...>Users</MudNavLink>
        }
        @if (_isOperatorOrAbove)
        {
            <MudNavLink Href="/settings/active-directory" ...>Active Directory</MudNavLink>
        }
    </MudNavGroup>
    <MudNavLink Href="/change-password" ...>Change Password</MudNavLink>
</MudNavMenu>

@code {
    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    private bool _isAdmin;
    private bool _isOperatorOrAbove;

    protected override async Task OnInitializedAsync()
    {
        if (AuthState != null)
        {
            var state = await AuthState;
            _isAdmin = state.User.IsInRole("Admin");
            _isOperatorOrAbove = state.User.IsInRole("Admin") || state.User.IsInRole("Operator");
        }
    }
}
```

NOTE: The Settings NavGroup should be visible if the user can see ANY setting underneath.
If only "Active Directory" is visible (Operator), show the group. If neither is visible
(Viewer), hide the entire group:

```razor
@if (_isOperatorOrAbove)
{
    <MudNavGroup Title="Settings" ...>
        @if (_isAdmin)
        {
            <MudNavLink Href="/settings/users" ...>Users</MudNavLink>
        }
        <MudNavLink Href="/settings/active-directory" ...>Active Directory</MudNavLink>
    </MudNavGroup>
}
```

### Layer 4 (Optional but Recommended): API Endpoint Protection

The REST API endpoints in `/api/` should also enforce policies for defense-in-depth.
Currently they use `.RequireAuthorization()` (any authenticated user).

For mutation endpoints (POST, PUT, DELETE), add policy requirements:

```csharp
// In ServiceAccountEndpoints.cs
group.MapPost("/", ...).RequireAuthorization("RequireAdmin");
group.MapPut("/{id:guid}", ...).RequireAuthorization("RequireAdmin");
group.MapDelete("/{id:guid}", ...).RequireAuthorization("RequireAdmin");
// GET endpoints stay as RequireAuthorization() (any authenticated)
```

Apply this pattern to these endpoint files:
- `ServiceAccountEndpoints.cs` — mutations Admin only
- `ToolServerEndpoints.cs` — mutations Admin only
- `AgentEndpoints.cs` — mutations Admin only
- `WorkflowEndpoints.cs` — mutations Admin only
- `RulesetEndpoints.cs` — mutations Admin only
- `ExampleSetEndpoints.cs` — mutations Admin only
- `CapabilityMappingEndpoints.cs` — mutations Admin only
- `ApiKeyEndpoints.cs` — mutations Admin only
- `CredentialEndpoints.cs` — mutations Admin only
- `ApprovalEndpoints.cs` — approve/reject: RequireOperator; others: as-is
- `ManualSubmissionEndpoints.cs` — submit: RequireOperator
- `AuditEventEndpoints.cs` — all GET, leave as RequireAuthorization()

## AccessDenied Page

There is already an `AccessDenied.razor` page. The cookie auth is configured with
`options.AccessDeniedPath = "/access-denied"`. When a user hits a page they can't
access, ASP.NET Core will redirect there automatically.

Verify the AccessDenied page shows a meaningful message like:
"You don't have permission to access this page. Contact your administrator if you
believe this is an error."

Include a "Go to Dashboard" button.

## Verification

### Test 1: Viewer role restrictions
1. Create a local user with Viewer role (via Settings/Users as admin)
2. Log in as that user
3. Verify: Dashboard, audit log, all list pages load with NO action buttons
4. Verify: NavMenu hides Approvals, Test Workflows, API Keys, Settings
5. Navigate directly to `/settings/users` → should redirect to `/access-denied`
6. Navigate directly to `/service-accounts/create` → should redirect to `/access-denied`
7. Navigate directly to `/manual-submissions` → should redirect to `/access-denied`

### Test 2: Operator role restrictions
1. Create a local user with Operator role
2. Log in as that user
3. Verify: All list pages load with NO create/edit/delete buttons
4. Verify: NavMenu shows Approvals and Test Workflows, hides API Keys and Settings/Users
5. Verify: Settings/Active Directory is visible and loads
6. Navigate directly to `/service-accounts/create` → should redirect to `/access-denied`
7. Navigate directly to `/settings/users` → should redirect to `/access-denied`
8. Verify: Manual Submissions / Test Workflows page works
9. Verify: Approvals page shows approve/reject buttons

### Test 3: Admin unchanged
1. Log in as admin
2. Verify: Everything works exactly as before — full access, all buttons visible

### Test 4: API endpoint protection
1. Create a JWT token for a Viewer user
2. `curl -X DELETE /api/service-accounts/{id}` → should return 403
3. `curl -X GET /api/service-accounts` → should return 200

## Files Changed (Summary)
- `Components/Layout/NavMenu.razor` — role-aware conditional rendering
- `Components/Pages/ServiceAccounts/Create.razor` — add RequireAdmin policy
- `Components/Pages/ServiceAccounts/Edit.razor` — add RequireAdmin policy
- `Components/Pages/ServiceAccounts/Index.razor` — conditional buttons
- `Components/Pages/Rulesets/Edit.razor` — add RequireAdmin policy
- `Components/Pages/Rulesets/Index.razor` — conditional buttons
- `Components/Pages/Examples/Edit.razor` — add RequireAdmin policy
- `Components/Pages/Examples/Index.razor` — conditional buttons
- `Components/Pages/Workflows/Edit.razor` — add RequireAdmin policy
- `Components/Pages/Workflows/Designer.razor` — add RequireAdmin policy
- `Components/Pages/Workflows/Index.razor` — conditional buttons
- `Components/Pages/Settings/Users.razor` — add RequireAdmin policy
- `Components/Pages/Settings/ActiveDirectory.razor` — add RequireOperator policy
- `Components/Pages/Approvals/Index.razor` — add RequireOperator policy
- `Components/Pages/ManualSubmissions.razor` — add RequireOperator policy
- `Components/Pages/ToolServers.razor` — conditional buttons
- `Components/Pages/Agents.razor` — conditional buttons
- `Components/Pages/CapabilityMappings.razor` — conditional buttons
- `Components/Pages/ApiKeys/Index.razor` — conditional buttons + RequireAdmin policy
- `Components/Pages/AccessDenied.razor` — verify/improve message
- ~10 Endpoint files — add policy to mutation routes
