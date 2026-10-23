using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusToClientAndUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the new columns as nullable first so existing rows can be backfilled from the
            // old ArchivedAt/IsActive columns before NOT NULL is enforced (see item #7 of
            // user_testing_defects.md / architecture.md's "Domain invariants: centralized status
            // transitions" for why Client/User are moving from ArchivedAt/IsActive to a proper
            // Status + StatusChangedAt pair, mirroring Invoice).
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Clients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAt",
                table: "Clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            // Data-migrate Clients: Status/StatusChangedAt derived from the old nullable
            // ArchivedAt timestamp. Removed where ArchivedAt was set (StatusChangedAt = that same
            // timestamp, preserving exactly when the client was archived); Active otherwise, with
            // StatusChangedAt defaulting to CreatedAtDate — a sensible "status has effectively
            // been Active since the row was created" timestamp for rows that never had an
            // ArchivedAt event (StatusChangedAt is non-nullable, so these rows need *some* value).
            migrationBuilder.Sql(
                """
                UPDATE "Clients"
                SET "Status" = CASE WHEN "ArchivedAt" IS NOT NULL THEN 'Removed' ELSE 'Active' END,
                    "StatusChangedAt" = COALESCE("ArchivedAt", "CreatedAtDate");
                """);

            // Data-migrate Users: Status derived from the old IsActive boolean. Unlike Client,
            // User has no CreatedAtDate (or any other row-creation timestamp) to fall back on, so
            // there's no natural "status changed" instant for rows that were always Active or
            // always Deactivated — default to the time this migration runs (now), the best
            // available approximation, and the same choice made for every existing row regardless
            // of its IsActive value.
            migrationBuilder.Sql(
                $"""
                UPDATE "Users"
                SET "Status" = CASE WHEN "IsActive" THEN 'Active' ELSE 'Deactivated' END,
                    "StatusChangedAt" = TIMESTAMPTZ '{DateTime.UtcNow:O}';
                """);

            // Every row now has a value in both new columns - enforce NOT NULL to match the
            // entity configuration (ClientConfiguration/UserConfiguration).
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Clients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "StatusChangedAt",
                table: "Clients",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "StatusChangedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            // Superseded by the new (BusinessId, Status) index below.
            migrationBuilder.DropIndex(
                name: "IX_Clients_BusinessId_ArchivedAt",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_BusinessId_Status",
                table: "Clients",
                columns: new[] { "BusinessId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clients_BusinessId_Status",
                table: "Clients");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql(
                """
                UPDATE "Clients"
                SET "ArchivedAt" = CASE WHEN "Status" = 'Removed' THEN "StatusChangedAt" ELSE NULL END;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Users"
                SET "IsActive" = CASE WHEN "Status" = 'Active' THEN TRUE ELSE FALSE END;
                """);

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_BusinessId_ArchivedAt",
                table: "Clients",
                columns: new[] { "BusinessId", "ArchivedAt" });
        }
    }
}
