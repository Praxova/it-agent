using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LucidAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnvelopeEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "SystemSecrets",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0008-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0009-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0010-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0011-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069), new DateTime(2026, 2, 16, 1, 17, 57, 417, DateTimeKind.Utc).AddTicks(7069) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "SystemSecrets");

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0008-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0009-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0010-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0011-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336), new DateTime(2026, 2, 16, 0, 44, 29, 547, DateTimeKind.Utc).AddTicks(6336) });
        }
    }
}
