using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LucidAdmin.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalRequests");

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852), new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852), new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852), new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852), new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852), new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852), new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852), new DateTime(2026, 2, 5, 15, 33, 42, 488, DateTimeKind.Utc).AddTicks(4852) });
        }
    }
}
