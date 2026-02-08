using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LucidAdmin.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agents_WorkflowDefinitions_WorkflowDefinitionId",
                table: "Agents");

            migrationBuilder.DropForeignKey(
                name: "FK_StepTransitions_WorkflowSteps_ToStepId",
                table: "StepTransitions");

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406) });

            migrationBuilder.InsertData(
                table: "Capabilities",
                columns: new[] { "Id", "CapabilityId", "Category", "ConfigurationExample", "ConfigurationSchema", "CreatedAt", "DependenciesJson", "Description", "DisplayName", "DocumentationUrl", "IsBuiltIn", "IsEnabled", "MinToolServerVersion", "RequiredProvidersJson", "RequiresServiceAccount", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { new Guid("a1b2c3d4-0008-0000-0000-000000000001"), "azure-user-lookup", "azure", null, null, new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), null, "Query Microsoft Entra ID for user details via Microsoft Graph API", "Look up user in Azure AD / Entra ID", null, true, true, null, "[\"azure\"]", true, new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), "1.0.0" },
                    { new Guid("a1b2c3d4-0009-0000-0000-000000000001"), "azure-vm-lookup", "azure", null, null, new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), null, "Query Azure Resource Manager for VM details including status, size, and IPs", "Look up virtual machine in Azure", null, true, true, null, "[\"azure\"]", true, new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), "1.0.0" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_WorkflowDefinitionId",
                table: "ApprovalRequests",
                column: "WorkflowDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agents_WorkflowDefinitions_WorkflowDefinitionId",
                table: "Agents",
                column: "WorkflowDefinitionId",
                principalTable: "WorkflowDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalRequests_WorkflowDefinitions_WorkflowDefinitionId",
                table: "ApprovalRequests",
                column: "WorkflowDefinitionId",
                principalTable: "WorkflowDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StepTransitions_WorkflowSteps_ToStepId",
                table: "StepTransitions",
                column: "ToStepId",
                principalTable: "WorkflowSteps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agents_WorkflowDefinitions_WorkflowDefinitionId",
                table: "Agents");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalRequests_WorkflowDefinitions_WorkflowDefinitionId",
                table: "ApprovalRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_StepTransitions_WorkflowSteps_ToStepId",
                table: "StepTransitions");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequests_WorkflowDefinitionId",
                table: "ApprovalRequests");

            migrationBuilder.DeleteData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0008-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0009-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777), new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777), new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777), new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777), new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777), new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777), new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777), new DateTime(2026, 2, 6, 19, 21, 33, 167, DateTimeKind.Utc).AddTicks(5777) });

            migrationBuilder.AddForeignKey(
                name: "FK_Agents_WorkflowDefinitions_WorkflowDefinitionId",
                table: "Agents",
                column: "WorkflowDefinitionId",
                principalTable: "WorkflowDefinitions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StepTransitions_WorkflowSteps_ToStepId",
                table: "StepTransitions",
                column: "ToStepId",
                principalTable: "WorkflowSteps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
