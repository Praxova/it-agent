using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LucidAdmin.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClarifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0008-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0009-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0010-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0011-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825), new DateTime(2026, 2, 8, 19, 38, 30, 934, DateTimeKind.Utc).AddTicks(9825) });

            migrationBuilder.CreateIndex(
                name: "IX_Clarifications_AgentName_Status",
                table: "Clarifications",
                columns: new[] { "AgentName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Clarifications_TicketId",
                table: "Clarifications",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clarifications");

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0008-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0009-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0010-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0011-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986) });
        }
    }
}
