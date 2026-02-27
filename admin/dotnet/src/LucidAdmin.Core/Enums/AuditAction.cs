namespace LucidAdmin.Core.Enums;

public enum AuditAction
{
    // Password operations
    PasswordReset,
    PasswordResetFailed,
    
    // Group operations
    GroupMemberAdded,
    GroupMemberRemoved,
    GroupOperationFailed,
    
    // File permission operations
    PermissionGranted,
    PermissionRevoked,
    PermissionOperationFailed,
    
    // Admin operations
    ServiceAccountCreated,
    ServiceAccountUpdated,
    ServiceAccountDeleted,
    ToolServerRegistered,
    ToolServerDeregistered,
    CapabilityMappingCreated,
    CapabilityMappingUpdated,
    CapabilityMappingDeleted,
    UserLogin,
    UserLogout,
    UserCreated,
    UserUpdated,
    UserDeleted,
    PasswordChanged,

    // Agent operations
    AgentCreated,
    AgentUpdated,
    AgentDeleted,
    AgentStarted,
    AgentStopped,
    AgentHeartbeat,

    // Connectivity tests
    ServiceAccountConnectivityTest,
    ToolServerConnectivityTest,

    // Credential operations
    CredentialUpdated,
    CredentialAccessed,
    CredentialDeleted,

    // API key operations
    ApiKeyCreated,
    ApiKeyRevoked,
    ApiKeyUsed,

    // Operation token operations
    OperationTokenIssued,
    OperationTokenDenied
}
