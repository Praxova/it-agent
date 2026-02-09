using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LucidAdmin.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticationSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthenticationSource",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0008-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0009-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0010-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0011-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746), new DateTime(2026, 2, 9, 15, 2, 41, 630, DateTimeKind.Utc).AddTicks(2746) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthenticationSource",
                table: "Users");

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0008-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0009-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0010-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0011-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936), new DateTime(2026, 2, 9, 14, 16, 59, 904, DateTimeKind.Utc).AddTicks(3936) });
        }
    }
}
