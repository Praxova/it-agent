// Pre-existing records will have SequenceNumber=0 and empty hash fields.
// The verification endpoint treats records with SequenceNumber=0 as pre-integrity.
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LucidAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditChainFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousRecordHash",
                table: "AuditEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecordHash",
                table: "AuditEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SequenceNumber",
                table: "AuditEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0008-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0009-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0010-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });

            migrationBuilder.UpdateData(
                table: "Capabilities",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-0011-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845), new DateTime(2026, 2, 27, 7, 36, 24, 99, DateTimeKind.Utc).AddTicks(7845) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousRecordHash",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "RecordHash",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "SequenceNumber",
                table: "AuditEvents");

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
        }
    }
}
