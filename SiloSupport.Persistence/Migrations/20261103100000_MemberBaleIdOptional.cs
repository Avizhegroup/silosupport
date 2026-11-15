using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiloSupport.Persistence.Migrations
{
    /// <summary>
    /// <c>CustomerMembers.BaleId</c> (Bale user name) becomes optional: empty values become NULL and the unique
    /// index is filtered to non-NULL values, so any number of members may have no Bale user name.
    /// Hand-authored (no Designer file) like the older migrations. SQL Server uses <c>AlterColumn</c>; SQLite (tests)
    /// replaces the column with plain SQL because it cannot alter a column.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261103100000_MemberBaleIdOptional")]
    public partial class MemberBaleIdOptional : Migration
    {
        private bool IsSqlServer => ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerMembers_BaleId",
                table: "CustomerMembers");

            if (IsSqlServer)
            {
                migrationBuilder.AlterColumn<string>(
                    name: "BaleId",
                    table: "CustomerMembers",
                    type: "nvarchar(64)",
                    maxLength: 64,
                    nullable: true,
                    oldClrType: typeof(string),
                    oldType: "nvarchar(64)",
                    oldMaxLength: 64);
            }
            else
            {
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" ADD COLUMN \"BaleIdNullable\" TEXT NULL");
                migrationBuilder.Sql("UPDATE \"CustomerMembers\" SET \"BaleIdNullable\" = \"BaleId\"");
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" DROP COLUMN \"BaleId\"");
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" RENAME COLUMN \"BaleIdNullable\" TO \"BaleId\"");
            }

            migrationBuilder.Sql("UPDATE \"CustomerMembers\" SET \"BaleId\" = NULL WHERE LTRIM(RTRIM(\"BaleId\")) = ''");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMembers_BaleId",
                table: "CustomerMembers",
                column: "BaleId",
                unique: true,
                filter: "[BaleId] IS NOT NULL");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Members without a Bale ID get a unique placeholder
            // ("member-<id>") so the column can be required again.
            migrationBuilder.DropIndex(
                name: "IX_CustomerMembers_BaleId",
                table: "CustomerMembers");

            migrationBuilder.Sql(
                IsSqlServer
                    ? "UPDATE [CustomerMembers] SET [BaleId] = CONCAT('member-', [Id]) WHERE [BaleId] IS NULL"
                    : "UPDATE \"CustomerMembers\" SET \"BaleId\" = 'member-' || \"Id\" WHERE \"BaleId\" IS NULL");

            if (IsSqlServer)
            {
                migrationBuilder.AlterColumn<string>(
                    name: "BaleId",
                    table: "CustomerMembers",
                    type: "nvarchar(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "",
                    oldClrType: typeof(string),
                    oldType: "nvarchar(64)",
                    oldMaxLength: 64,
                    oldNullable: true);
            }
            else
            {
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" ADD COLUMN \"BaleIdRequired\" TEXT NOT NULL DEFAULT ''");
                migrationBuilder.Sql("UPDATE \"CustomerMembers\" SET \"BaleIdRequired\" = \"BaleId\"");
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" DROP COLUMN \"BaleId\"");
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" RENAME COLUMN \"BaleIdRequired\" TO \"BaleId\"");
            }

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMembers_BaleId",
                table: "CustomerMembers",
                column: "BaleId",
                unique: true);
        }
    }
}
