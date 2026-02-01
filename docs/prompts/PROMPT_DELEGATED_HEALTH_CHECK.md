# Claude Code Prompt: Delegated Service Account Health Check

## Context

Currently, the Admin Portal attempts to perform service account health checks (like testing AD connectivity) directly from the portal itself. This fails when the Admin Portal runs on a machine that isn't domain-joined or doesn't have direct access to the target system.

The correct architecture is to **delegate health checks to the Tool Server** that will actually use those credentials. The Tool Server is the component that performs AD operations, so it should be the one testing AD connectivity.

## Current State

- Admin Portal: `/admin/dotnet/src/LucidAdmin.Web/`
- Tool Server: `/tool-server/dotnet/src/LucidToolServer/`
- Service Account health check currently tries to connect directly from Admin Portal
- This fails for AD service accounts when Admin Portal isn't domain-joined

## Requirements

### 1. Tool Server: Add Health Check Endpoint

**Create endpoint:** `POST /api/v1/health/test-connection`

This endpoint accepts service account details and tests connectivity to the target system.

**File:** `/tool-server/dotnet/src/LucidToolServer/Endpoints/HealthEndpoints.cs` (update existing or create)

```csharp
public record TestConnectionRequest(
    string ProviderType,        // "windows-ad", "ldap", etc.
    string? Domain,             // For AD: "montanifarms.com"
    string? Server,             // Optional: specific server to test
    string? Username,           // Service account username
    string? Password,           // Service account password (for testing only)
    Dictionary<string, string>? AdditionalConfig  // Provider-specific config
);

public record TestConnectionResponse(
    bool Success,
    string Message,
    string? Details,            // Additional info (e.g., DC name, response time)
    DateTime TestedAt
);
```

**Endpoint logic:**
```csharp
app.MapPost("/api/v1/health/test-connection", async (TestConnectionRequest request) =>
{
    try
    {
        switch (request.ProviderType.ToLower())
        {
            case "windows-ad":
                return await TestActiveDirectoryConnection(request);
            // Future: case "ldap":, case "sql-server":, etc.
            default:
                return Results.BadRequest(new TestConnectionResponse(
                    false, 
                    $"Unsupported provider type: {request.ProviderType}",
                    null,
                    DateTime.UtcNow
                ));
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new TestConnectionResponse(
            false,
            "Connection test failed",
            ex.Message,
            DateTime.UtcNow
        ));
    }
});
```

**AD Test Implementation:**
```csharp
private static async Task<IResult> TestActiveDirectoryConnection(TestConnectionRequest request)
{
    if (string.IsNullOrEmpty(request.Domain))
        return Results.BadRequest(new TestConnectionResponse(false, "Domain is required", null, DateTime.UtcNow));
    
    try
    {
        using var context = new PrincipalContext(
            ContextType.Domain,
            request.Domain,
            request.Username,
            request.Password
        );
        
        // Try to validate credentials by querying something simple
        var isValid = context.ValidateCredentials(request.Username, request.Password);
        
        if (isValid)
        {
            return Results.Ok(new TestConnectionResponse(
                true,
                $"Successfully connected to domain {request.Domain}",
                $"Connected server: {context.ConnectedServer}",
                DateTime.UtcNow
            ));
        }
        else
        {
            return Results.Ok(new TestConnectionResponse(
                false,
                "Invalid credentials",
                "Username or password is incorrect",
                DateTime.UtcNow
            ));
        }
    }
    catch (PrincipalServerDownException ex)
    {
        return Results.Ok(new TestConnectionResponse(
            false,
            $"Cannot reach domain controller for {request.Domain}",
            ex.Message,
            DateTime.UtcNow
        ));
    }
    catch (Exception ex)
    {
        return Results.Ok(new TestConnectionResponse(
            false,
            "Connection test failed",
            ex.Message,
            DateTime.UtcNow
        ));
    }
}
```

### 2. Admin Portal: Update Health Check Logic

**File:** `/admin/dotnet/src/LucidAdmin.Web/Endpoints/CredentialEndpoints.cs` or wherever health check is currently implemented

**New Logic Flow:**

```
1. User clicks "Test Connection" on a Service Account
2. Admin Portal checks: Is there a Tool Server with capability mapping for this provider type?
   - For "windows-ad" → look for tool servers with "ad-*" capabilities
   - For "servicenow-*" → test directly (Admin Portal can do HTTP)
   - For "llm-*" → test directly (Admin Portal can do HTTP)
3. If Tool Server found:
   - Forward test request to Tool Server's /api/v1/health/test-connection
   - Return result to UI
4. If no Tool Server found:
   - Return message: "No tool server configured for this provider type. Create a capability mapping first."
```

**Update or create endpoint:** `POST /api/v1/service-accounts/{id}/test`

```csharp
app.MapPost("/api/v1/service-accounts/{id:guid}/test", async (
    Guid id,
    IServiceAccountRepository serviceAccountRepo,
    ICapabilityMappingRepository capabilityRepo,
    IToolServerRepository toolServerRepo,
    IHttpClientFactory httpClientFactory,
    LucidDbContext db) =>
{
    var serviceAccount = await serviceAccountRepo.GetByIdAsync(id);
    if (serviceAccount is null)
        return Results.NotFound(new { error = "ServiceAccountNotFound" });
    
    var providerType = serviceAccount.ProviderType.ToLower();
    
    // Direct test for HTTP-based providers (ServiceNow, LLMs)
    if (providerType.StartsWith("servicenow-") || providerType.StartsWith("llm-"))
    {
        return await TestDirectConnection(serviceAccount, httpClientFactory);
    }
    
    // Delegated test for AD/infrastructure providers
    if (providerType == "windows-ad")
    {
        // Find a tool server that can test this
        var toolServer = await FindToolServerForProvider(providerType, capabilityRepo, toolServerRepo, db);
        
        if (toolServer is null)
        {
            return Results.Ok(new TestConnectionResponse(
                false,
                "No tool server available to test this connection",
                "Create a Tool Server and Capability Mapping for AD operations first. " +
                "The tool server will perform the actual AD health check.",
                DateTime.UtcNow
            ));
        }
        
        // Delegate to tool server
        return await DelegateTestToToolServer(serviceAccount, toolServer, httpClientFactory);
    }
    
    return Results.BadRequest(new { error = "UnsupportedProviderType" });
});

private static async Task<ToolServer?> FindToolServerForProvider(
    string providerType,
    ICapabilityMappingRepository capabilityRepo,
    IToolServerRepository toolServerRepo,
    LucidDbContext db)
{
    // For windows-ad, look for any tool server with ad-* capabilities
    if (providerType == "windows-ad")
    {
        var mapping = await db.CapabilityMappings
            .Include(m => m.ToolServer)
            .Where(m => m.ToolServer != null && 
                        m.ToolServer.IsEnabled &&
                        m.CapabilityName.StartsWith("ad-"))
            .FirstOrDefaultAsync();
        
        return mapping?.ToolServer;
    }
    
    return null;
}

private static async Task<IResult> DelegateTestToToolServer(
    ServiceAccount serviceAccount,
    ToolServer toolServer,
    IHttpClientFactory httpClientFactory)
{
    var client = httpClientFactory.CreateClient();
    client.BaseAddress = new Uri(toolServer.Url.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(30);
    
    // Build the test request
    // Note: In production, you'd decrypt credentials from vault/secure storage
    var testRequest = new
    {
        ProviderType = serviceAccount.ProviderType,
        Domain = serviceAccount.ProviderConfig?.GetValueOrDefault("domain"),
        Server = serviceAccount.ProviderConfig?.GetValueOrDefault("server"),
        Username = serviceAccount.ProviderConfig?.GetValueOrDefault("username"),
        // Password would come from secure credential storage
        Password = "CREDENTIAL_LOOKUP_NEEDED", // TODO: Implement secure credential retrieval
        AdditionalConfig = serviceAccount.ProviderConfig
    };
    
    try
    {
        var response = await client.PostAsJsonAsync("/api/v1/health/test-connection", testRequest);
        
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<TestConnectionResponse>();
            return Results.Ok(result);
        }
        else
        {
            return Results.Ok(new TestConnectionResponse(
                false,
                $"Tool server returned error: {response.StatusCode}",
                await response.Content.ReadAsStringAsync(),
                DateTime.UtcNow
            ));
        }
    }
    catch (HttpRequestException ex)
    {
        return Results.Ok(new TestConnectionResponse(
            false,
            $"Cannot reach tool server at {toolServer.Url}",
            ex.Message,
            DateTime.UtcNow
        ));
    }
}
```

### 3. Admin Portal UI: Update Test Button and Messaging

**File:** Service Account dialog/form component (likely `ServiceAccountDialog.razor` or similar)

**Update the Test Connection button behavior:**

```razor
@if (_testResult != null)
{
    <MudAlert Severity="@(_testResult.Success ? Severity.Success : Severity.Warning)" 
              Class="mt-2" Dense="true">
        <MudText Typo="Typo.body2">@_testResult.Message</MudText>
        @if (!string.IsNullOrEmpty(_testResult.Details))
        {
            <MudText Typo="Typo.caption">@_testResult.Details</MudText>
        }
    </MudAlert>
}

@if (_providerRequiresToolServer && !_hasToolServerMapping)
{
    <MudAlert Severity="Severity.Info" Class="mt-2" Dense="true">
        <MudText Typo="Typo.body2">
            <MudIcon Icon="@Icons.Material.Filled.Info" Size="Size.Small" Class="mr-1" />
            Testing AD connections requires a Tool Server with capability mappings.
        </MudText>
        <MudLink Href="/tool-servers">Configure Tool Server</MudLink> | 
        <MudLink Href="/capability-mappings">Configure Capability Mappings</MudLink>
    </MudAlert>
}
```

**Add logic to detect if provider requires tool server:**

```csharp
private bool _providerRequiresToolServer => 
    _providerType?.ToLower() == "windows-ad";

private bool _hasToolServerMapping = false;

protected override async Task OnInitializedAsync()
{
    // ... existing code ...
    
    // Check if tool server mapping exists for AD providers
    if (_providerRequiresToolServer)
    {
        _hasToolServerMapping = await CheckToolServerMapping();
    }
}

private async Task<bool> CheckToolServerMapping()
{
    try
    {
        // Call API to check if any AD capability mappings exist
        var response = await Http.GetAsync("/api/v1/capability-mappings?capability=ad-password-reset");
        if (response.IsSuccessStatusCode)
        {
            var mappings = await response.Content.ReadFromJsonAsync<List<CapabilityMappingResponse>>();
            return mappings?.Any() ?? false;
        }
    }
    catch { }
    return false;
}
```

### 4. Add Capability Mapping Query Endpoint (if not exists)

**File:** `/admin/dotnet/src/LucidAdmin.Web/Endpoints/CapabilityMappingEndpoints.cs`

Add filter support:
```csharp
app.MapGet("/api/v1/capability-mappings", async (
    [FromQuery] string? capability,
    ICapabilityMappingRepository repo) =>
{
    var mappings = await repo.GetAllAsync();
    
    if (!string.IsNullOrEmpty(capability))
    {
        mappings = mappings.Where(m => 
            m.CapabilityName.Equals(capability, StringComparison.OrdinalIgnoreCase) ||
            m.CapabilityName.StartsWith(capability.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
        );
    }
    
    return Results.Ok(mappings.Select(m => MapToResponse(m)));
});
```

## Testing

1. **Without Tool Server configured:**
   - Create a `windows-ad` service account
   - Click "Test Connection"
   - Should see message: "No tool server available..." with links to configure

2. **With Tool Server but no capability mapping:**
   - Same as above - should still show the message

3. **With Tool Server AND capability mapping:**
   - Click "Test Connection"
   - Should delegate to Tool Server
   - Tool Server performs actual AD test
   - Result returned to Admin Portal UI

4. **ServiceNow/LLM accounts:**
   - Should still test directly (no tool server needed)
   - These use HTTP which Admin Portal can do directly

## Security Considerations

- The Tool Server health check endpoint should only accept requests from trusted Admin Portal IPs
- Credentials passed for testing should be encrypted in transit (HTTPS)
- Consider adding API key authentication between Admin Portal and Tool Server
- Don't log credentials in plain text

## Files to Modify

### Tool Server
- `tool-server/dotnet/src/LucidToolServer/Endpoints/HealthEndpoints.cs` - Add test-connection endpoint

### Admin Portal  
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/CredentialEndpoints.cs` - Update health check logic
- `admin/dotnet/src/LucidAdmin.Web/Components/Pages/ServiceAccounts/*.razor` - Update UI messaging
- `admin/dotnet/src/LucidAdmin.Web/Endpoints/CapabilityMappingEndpoints.cs` - Add filter support if needed

## Summary

This change makes the architecture more correct by:
1. Having health checks performed by the component that will actually use the credentials
2. Providing clear guidance when prerequisites aren't met
3. Keeping direct HTTP tests (ServiceNow, LLMs) in Admin Portal where they work fine
4. Delegating infrastructure tests (AD, LDAP) to appropriately-positioned Tool Servers
