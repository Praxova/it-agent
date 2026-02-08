using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LucidAdmin.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkstationCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.InsertData(
                table: "Capabilities",
                columns: new[] { "Id", "CapabilityId", "Category", "ConfigurationExample", "ConfigurationSchema", "CreatedAt", "DependenciesJson", "Description", "DisplayName", "DocumentationUrl", "IsBuiltIn", "IsEnabled", "MinToolServerVersion", "RequiredProvidersJson", "RequiresServiceAccount", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { new Guid("a1b2c3d4-0010-0000-0000-000000000001"), "ad-computer-lookup", "active-directory", null, null, new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), null, "Query Active Directory for computer objects assigned to a user via managedBy attribute", "Look up user's assigned computer(s)", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), "1.0.0" },
                    { new Guid("a1b2c3d4-0011-0000-0000-000000000001"), "remote-software-install", "workstation-management", null, null, new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), null, "Install software packages on remote Windows computers via PowerShell Remoting (WinRM)", "Install software on remote computer", null, true, true, null, "[\"windows-ad\"]", true, new DateTime(2026, 2, 8, 17, 18, 6, 982, DateTimeKind.Utc).AddTicks(8986), "1.0.0" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0010-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0011-0000-0000-000000000001"));

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

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0008-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0009-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406), new DateTime(2026, 2, 8, 16, 49, 14, 580, DateTimeKind.Utc).AddTicks(3406) });
        }
    }
}
