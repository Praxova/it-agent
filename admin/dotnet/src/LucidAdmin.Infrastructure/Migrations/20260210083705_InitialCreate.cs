using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LucidAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Capabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapabilityId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    RequiresServiceAccount = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiredProvidersJson = table.Column<string>(type: "TEXT", nullable: true),
                    DependenciesJson = table.Column<string>(type: "TEXT", nullable: true),
                    MinToolServerVersion = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ConfigurationSchema = table.Column<string>(type: "TEXT", nullable: true),
                    ConfigurationExample = table.Column<string>(type: "TEXT", nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DocumentationUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Capabilities", x => x.Id);
                    table.UniqueConstraint("AK_Capabilities_CapabilityId", x => x.CapabilityId);
                });

            migrationBuilder.CreateTable(
                name: "Clarifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WorkflowName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StepName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TicketId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TicketSysId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Question = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    PostedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UserReply = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ContextSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResumeAfterStep = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clarifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rulesets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rulesets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AccountType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Configuration = table.Column<string>(type: "TEXT", nullable: true),
                    CredentialStorage = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialReference = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    EncryptedCredentials = table.Column<byte[]>(type: "BLOB", nullable: true),
                    CredentialNonce = table.Column<byte[]>(type: "BLOB", nullable: true),
                    CredentialsUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    HealthStatus = table.Column<string>(type: "TEXT", nullable: false),
                    LastHealthCheck = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastHealthMessage = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Color = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToolServers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastHeartbeat = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ApiKeyHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    AuthenticationSource = table.Column<string>(type: "TEXT", nullable: false),
                    LastLogin = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RulesetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RuleText = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rules_Rulesets_RulesetId",
                        column: x => x.RulesetId,
                        principalTable: "Rulesets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExampleSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TicketCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExampleSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExampleSets_TicketCategories_TicketCategoryId",
                        column: x => x.TicketCategoryId,
                        principalTable: "TicketCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CapabilityMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServiceAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToolServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapabilityId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CapabilityVersion = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Configuration = table.Column<string>(type: "TEXT", nullable: true),
                    AllowedScopesJson = table.Column<string>(type: "TEXT", nullable: true),
                    DeniedScopesJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    HealthStatus = table.Column<string>(type: "TEXT", nullable: false),
                    LastHealthCheck = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastHealthMessage = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapabilityMappings_Capabilities_CapabilityId",
                        column: x => x.CapabilityId,
                        principalTable: "Capabilities",
                        principalColumn: "CapabilityId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapabilityMappings_ServiceAccounts_ServiceAccountId",
                        column: x => x.ServiceAccountId,
                        principalTable: "ServiceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CapabilityMappings_ToolServers_ToolServerId",
                        column: x => x.ToolServerId,
                        principalTable: "ToolServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Examples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExampleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TicketShortDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TicketDescription = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CallerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TicketCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpectedConfidence = table.Column<decimal>(type: "TEXT", precision: 3, scale: 2, nullable: false),
                    ExpectedAffectedUser = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ExpectedTargetGroup = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ExpectedTargetResource = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ExpectedPermissionLevel = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ExpectedShouldEscalate = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpectedEscalationReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Examples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Examples_ExampleSets_ExampleSetId",
                        column: x => x.ExampleSetId,
                        principalTable: "ExampleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Examples_TicketCategories_TicketCategoryId",
                        column: x => x.TicketCategoryId,
                        principalTable: "TicketCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LayoutJson = table.Column<string>(type: "TEXT", nullable: true),
                    TriggerType = table.Column<string>(type: "TEXT", nullable: true),
                    TriggerConfigJson = table.Column<string>(type: "TEXT", nullable: true),
                    ExampleSetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowDefinitions_ExampleSets_ExampleSetId",
                        column: x => x.ExampleSetId,
                        principalTable: "ExampleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    HostName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastActivity = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastHeartbeat = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TicketsProcessed = table.Column<int>(type: "INTEGER", nullable: false),
                    LlmServiceAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ServiceNowAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssignmentGroup = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    WorkflowDefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Agents_ServiceAccounts_LlmServiceAccountId",
                        column: x => x.LlmServiceAccountId,
                        principalTable: "ServiceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Agents_ServiceAccounts_ServiceNowAccountId",
                        column: x => x.ServiceNowAccountId,
                        principalTable: "ServiceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Agents_WorkflowDefinitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    StepName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AgentName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TicketId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TicketShortDescription = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ProposedAction = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ContextSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResumeAfterStep = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AutoApproveThreshold = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: true),
                    Confidence = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: true),
                    WasAutoApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DecidedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TimeoutMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_WorkflowDefinitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRulesetMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RulesetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRulesetMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowRulesetMappings_Rulesets_RulesetId",
                        column: x => x.RulesetId,
                        principalTable: "Rulesets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowRulesetMappings_WorkflowDefinitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StepType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: true),
                    PositionX = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionY = table.Column<int>(type: "INTEGER", nullable: false),
                    DrawflowNodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowSteps_WorkflowDefinitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentServiceAccountBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServiceAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Qualifier = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentServiceAccountBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentServiceAccountBindings_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentServiceAccountBindings_ServiceAccounts_ServiceAccountId",
                        column: x => x.ServiceAccountId,
                        principalTable: "ServiceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastUsedFromIp = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    RevocationReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    IpRestrictions = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Metadata = table.Column<string>(type: "TEXT", nullable: true),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ToolServerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AllowedServiceAccountIds = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeys_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApiKeys_ToolServers_ToolServerId",
                        column: x => x.ToolServerId,
                        principalTable: "ToolServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToolServerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    CapabilityId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PerformedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    TargetResource = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    TicketNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DetailsJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AuditEvents_ToolServers_ToolServerId",
                        column: x => x.ToolServerId,
                        principalTable: "ToolServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ManualSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Requester = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ExtraDataJson = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PickedUpAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResultStatus = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ResultMessage = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ResultDetailsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualSubmissions_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StepRulesetMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowStepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RulesetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepRulesetMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StepRulesetMappings_Rulesets_RulesetId",
                        column: x => x.RulesetId,
                        principalTable: "Rulesets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StepRulesetMappings_WorkflowSteps_WorkflowStepId",
                        column: x => x.WorkflowStepId,
                        principalTable: "WorkflowSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StepTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromStepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToStepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Condition = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OutputIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    InputIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StepTransitions_WorkflowSteps_FromStepId",
                        column: x => x.FromStepId,
                        principalTable: "WorkflowSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StepTransitions_WorkflowSteps_ToStepId",
                        column: x => x.ToStepId,
                        principalTable: "WorkflowSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Capabilities",
                columns: new[] { "Id", "CapabilityId", "Category", "ConfigurationExample", "ConfigurationSchema", "CreatedAt", "DependenciesJson", "Description", "DisplayName", "DocumentationUrl", "IsBuiltIn", "IsEnabled", "MinToolServerVersion", "RequiredProvidersJson", "RequiresServiceAccount", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { new Guid("a1b2c3d4-0001-0000-0000-000000000001"), "ad-password-reset", "active-directory", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Reset Active Directory user passwords with validation", "Reset user passwords in Active Directory", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" },
                    { new Guid("a1b2c3d4-0002-0000-0000-000000000001"), "ad-group-add", "active-directory", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Add a user to an Active Directory security or distribution group", "Add user to Active Directory group", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" },
                    { new Guid("a1b2c3d4-0003-0000-0000-000000000001"), "ad-group-remove", "active-directory", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Remove a user from an Active Directory security or distribution group", "Remove user from Active Directory group", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" },
                    { new Guid("a1b2c3d4-0004-0000-0000-000000000001"), "ad-user-lookup", "active-directory", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Query Active Directory for user information and attributes", "Look up user information in Active Directory", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" },
                    { new Guid("a1b2c3d4-0005-0000-0000-000000000001"), "ad-user-unlock", "active-directory", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Unlock a locked Active Directory user account", "Unlock a locked Active Directory account", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" },
                    { new Guid("a1b2c3d4-0006-0000-0000-000000000001"), "ntfs-permission-grant", "file-system", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Grant NTFS permissions to users or groups for files and folders", "Grant NTFS file/folder permissions", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" },
                    { new Guid("a1b2c3d4-0007-0000-0000-000000000001"), "ntfs-permission-revoke", "file-system", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Revoke NTFS permissions from users or groups for files and folders", "Revoke NTFS file/folder permissions", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" },
                    { new Guid("a1b2c3d4-0008-0000-0000-000000000001"), "azure-user-lookup", "azure", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Query Microsoft Entra ID for user details via Microsoft Graph API", "Look up user in Azure AD / Entra ID", null, true, true, null, "[\"azure\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" },
                    { new Guid("a1b2c3d4-0009-0000-0000-000000000001"), "azure-vm-lookup", "azure", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Query Azure Resource Manager for VM details including status, size, and IPs", "Look up virtual machine in Azure", null, true, true, null, "[\"azure\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" },
                    { new Guid("a1b2c3d4-0010-0000-0000-000000000001"), "ad-computer-lookup", "active-directory", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Query Active Directory for computer objects assigned to a user via managedBy attribute", "Look up user's assigned computer(s)", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" },
                    { new Guid("a1b2c3d4-0011-0000-0000-000000000001"), "remote-software-install", "workstation-management", null, null, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), null, "Install software packages on remote Windows computers via PowerShell Remoting (WinRM)", "Install software on remote computer", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 10, 8, 37, 5, 600, DateTimeKind.Utc).AddTicks(5323), "1.0.0" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_HostName",
                table: "Agents",
                column: "HostName");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_LlmServiceAccountId",
                table: "Agents",
                column: "LlmServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_Name",
                table: "Agents",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_ServiceNowAccountId",
                table: "Agents",
                column: "ServiceNowAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_Status",
                table: "Agents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_WorkflowDefinitionId",
                table: "Agents",
                column: "WorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentServiceAccountBindings_AgentId_Role_Qualifier",
                table: "AgentServiceAccountBindings",
                columns: new[] { "AgentId", "Role", "Qualifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentServiceAccountBindings_ServiceAccountId",
                table: "AgentServiceAccountBindings",
                column: "ServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_AgentId",
                table: "ApiKeys",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_IsActive",
                table: "ApiKeys",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyPrefix",
                table: "ApiKeys",
                column: "KeyPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_ToolServerId",
                table: "ApiKeys",
                column: "ToolServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_AgentName_Status",
                table: "ApprovalRequests",
                columns: new[] { "AgentName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Status",
                table: "ApprovalRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_TicketId",
                table: "ApprovalRequests",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_WorkflowDefinitionId",
                table: "ApprovalRequests",
                column: "WorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Action",
                table: "AuditEvents",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_AgentId",
                table: "AuditEvents",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CapabilityId",
                table: "AuditEvents",
                column: "CapabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CreatedAt",
                table: "AuditEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ToolServerId",
                table: "AuditEvents",
                column: "ToolServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Capabilities_CapabilityId",
                table: "Capabilities",
                column: "CapabilityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Capabilities_Category",
                table: "Capabilities",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityMappings_CapabilityId",
                table: "CapabilityMappings",
                column: "CapabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityMappings_ServiceAccountId_ToolServerId_CapabilityId",
                table: "CapabilityMappings",
                columns: new[] { "ServiceAccountId", "ToolServerId", "CapabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityMappings_ToolServerId",
                table: "CapabilityMappings",
                column: "ToolServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Clarifications_AgentName_Status",
                table: "Clarifications",
                columns: new[] { "AgentName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Clarifications_TicketId",
                table: "Clarifications",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_Examples_ExampleSetId_Name",
                table: "Examples",
                columns: new[] { "ExampleSetId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Examples_TicketCategoryId",
                table: "Examples",
                column: "TicketCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ExampleSets_Name",
                table: "ExampleSets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExampleSets_TicketCategoryId",
                table: "ExampleSets",
                column: "TicketCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualSubmissions_AgentId_Status",
                table: "ManualSubmissions",
                columns: new[] { "AgentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualSubmissions_SubmittedAt",
                table: "ManualSubmissions",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_IsActive",
                table: "Rules",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_Priority",
                table: "Rules",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_RulesetId",
                table: "Rules",
                column: "RulesetId");

            migrationBuilder.CreateIndex(
                name: "IX_Rulesets_Category",
                table: "Rulesets",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Rulesets_IsActive",
                table: "Rulesets",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Rulesets_IsBuiltIn",
                table: "Rulesets",
                column: "IsBuiltIn");

            migrationBuilder.CreateIndex(
                name: "IX_Rulesets_Name",
                table: "Rulesets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAccounts_Name",
                table: "ServiceAccounts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAccounts_Provider",
                table: "ServiceAccounts",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_StepRulesetMappings_RulesetId",
                table: "StepRulesetMappings",
                column: "RulesetId");

            migrationBuilder.CreateIndex(
                name: "IX_StepRulesetMappings_WorkflowStepId_RulesetId",
                table: "StepRulesetMappings",
                columns: new[] { "WorkflowStepId", "RulesetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StepTransitions_FromStepId_ToStepId_OutputIndex",
                table: "StepTransitions",
                columns: new[] { "FromStepId", "ToStepId", "OutputIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StepTransitions_ToStepId",
                table: "StepTransitions",
                column: "ToStepId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketCategories_Name",
                table: "TicketCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToolServers_Endpoint",
                table: "ToolServers",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToolServers_Name",
                table: "ToolServers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_ExampleSetId",
                table: "WorkflowDefinitions",
                column: "ExampleSetId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_Name",
                table: "WorkflowDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRulesetMappings_RulesetId",
                table: "WorkflowRulesetMappings",
                column: "RulesetId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRulesetMappings_WorkflowDefinitionId_RulesetId",
                table: "WorkflowRulesetMappings",
                columns: new[] { "WorkflowDefinitionId", "RulesetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_WorkflowDefinitionId_Name",
                table: "WorkflowSteps",
                columns: new[] { "WorkflowDefinitionId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentServiceAccountBindings");

            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "ApprovalRequests");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "CapabilityMappings");

            migrationBuilder.DropTable(
                name: "Clarifications");

            migrationBuilder.DropTable(
                name: "Examples");

            migrationBuilder.DropTable(
                name: "ManualSubmissions");

            migrationBuilder.DropTable(
                name: "Rules");

            migrationBuilder.DropTable(
                name: "StepRulesetMappings");

            migrationBuilder.DropTable(
                name: "StepTransitions");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WorkflowRulesetMappings");

            migrationBuilder.DropTable(
                name: "Capabilities");

            migrationBuilder.DropTable(
                name: "ToolServers");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropTable(
                name: "WorkflowSteps");

            migrationBuilder.DropTable(
                name: "Rulesets");

            migrationBuilder.DropTable(
                name: "ServiceAccounts");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitions");

            migrationBuilder.DropTable(
                name: "ExampleSets");

            migrationBuilder.DropTable(
                name: "TicketCategories");
        }
    }
}
