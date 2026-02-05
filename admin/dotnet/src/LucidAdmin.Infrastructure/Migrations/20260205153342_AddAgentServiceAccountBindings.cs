using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LucidAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentServiceAccountBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_AgentServiceAccountBindings_AgentId_Role_Qualifier",
                table: "AgentServiceAccountBindings",
                columns: new[] { "AgentId", "Role", "Qualifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentServiceAccountBindings_ServiceAccountId",
                table: "AgentServiceAccountBindings",
                column: "ServiceAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentServiceAccountBindings");

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423), new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423), new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423), new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423), new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423), new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423), new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423), new DateTime(2026, 2, 5, 11, 57, 43, 94, DateTimeKind.Utc).AddTicks(4423) });
        }
    }
}
