# ADR-009: LLM Reasons, Tools Execute Pattern

## Status
Accepted

## Date
2026-01-30

## Context

During end-to-end testing, we discovered that putting "smart" logic in tools 
(e.g., fuzzy user matching in the .NET Tool Server) creates problems:

- **Auditability**: Hard to explain why the tool chose a particular user
- **Testing**: Fuzzy logic is hard to unit test comprehensively  
- **Security**: Tool makes assumptions without human-reviewable reasoning
- **Flexibility**: Changing matching logic requires code changes and redeployment

Meanwhile, the LLM excels at exactly these ambiguous decisions:
- "Han Solo" → probably means the user `hsolo` 
- "VPN access" → probably means the `VPN-Sales` group based on user's department
- "File share access" → needs clarification - which share? read or write?

## Decision

**Separate concerns between LLM (reasoning) and Tools (execution):**

### LLM Layer Responsibilities
- Parse natural language requests
- Resolve ambiguous references (names → samAccountNames)
- Select appropriate resources (which group, which share)
- Handle disambiguation ("Did you mean X or Y?")
- Make judgment calls based on context

### Tool Layer Responsibilities  
- Execute precise, validated commands
- No guessing or fuzzy matching
- Return success/failure with details
- Provide query endpoints for LLM to gather information

### Tool Types

**Query Tools** (read-only, inform LLM):
- `GET /user/search?q=...` - Find users matching a query
- `GET /groups?category=...` - List available groups
- `GET /user/{sam}/groups` - Get user's current memberships
- `GET /permissions/{path}` - List current permissions

**Action Tools** (execute changes):
- `POST /password/reset` - Reset password (exact samAccountName required)
- `POST /groups/add-member` - Add to group (exact names required)
- `POST /permissions/grant` - Grant access (exact parameters required)

### Example Flow
```
Ticket: "Han Solo needs VPN access"

1. LLM calls: GET /user/search?q=Han+Solo
   → Returns: [{samAccountName: "hsolo", displayName: "Han Solo", dept: "Shipping"}]

2. LLM calls: GET /groups?category=VPN  
   → Returns: [{name: "VPN-General"}, {name: "VPN-Shipping"}, {name: "VPN-Admin"}]

3. LLM reasons: User is in Shipping dept → VPN-Shipping is appropriate

4. LLM calls: POST /groups/add-member {samAccountName: "hsolo", groupName: "VPN-Shipping"}
   → Returns: {success: true}

5. LLM closes ticket with clear audit trail
```

## Consequences

### Positive
- Clear separation of concerns
- Auditable decision chain (LLM reasoning is logged)
- Tools are simple, testable, secure
- Easy to add new query endpoints without changing action logic
- LLM can ask for clarification when truly ambiguous

### Negative  
- More API calls per ticket (query + action)
- Requires good query endpoints in Tool Server
- LLM must be prompted to use two-step pattern

### Neutral
- Shifts complexity from code to prompts/examples
- May need to update classifier prompts to output intermediate steps

## References
- End-to-end testing session 2026-01-30
- Related: ADR-007 Capability Routing
