using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LucidAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InternalPki : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssuedCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SubjectCN = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SubjectAlternativeNames = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Thumbprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NotBefore = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NotAfter = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Usage = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IssuedTo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CertPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    KeyPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RenewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReplacedByThumbprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedCertificates", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0008-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0009-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0010-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0011-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215), new DateTime(2026, 2, 16, 2, 17, 33, 764, DateTimeKind.Utc).AddTicks(4215) });

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCertificates_Name_IsActive",
                table: "IssuedCertificates",
                columns: new[] { "Name", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssuedCertificates");

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
    }
}
