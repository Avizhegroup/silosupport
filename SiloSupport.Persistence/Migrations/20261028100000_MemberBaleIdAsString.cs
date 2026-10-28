using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiloSupport.Persistence.Migrations
{
    /// <summary>
    /// <c>CustomerMembers.BaleId</c> changes from a 64-bit integer to text (max 64 characters). Existing numeric ids are
    /// converted to their decimal text. The unique index is dropped and recreated around the column change.
    /// Hand-authored (no Designer file) like <see cref="InitialCreate"/>. SQL Server uses <c>AlterColumn</c>;
    /// SQLite (used by the tests) cannot alter a column without a table rebuild that needs the target model, so
    /// there the column is replaced with plain SQL: add a text column, copy the converted values, drop the old
    /// column and rename the new one.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261028100000_MemberBaleIdAsString")]
    public partial class MemberBaleIdAsString : Migration
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
                    maxLength: 64,
                    nullable: false,
                    oldClrType: typeof(long));
            }
            else
            {
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" ADD COLUMN \"BaleIdText\" TEXT NOT NULL DEFAULT ''");
                migrationBuilder.Sql("UPDATE \"CustomerMembers\" SET \"BaleIdText\" = CAST(\"BaleId\" AS TEXT)");
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" DROP COLUMN \"BaleId\"");
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" RENAME COLUMN \"BaleIdText\" TO \"BaleId\"");
            }

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMembers_BaleId",
                table: "CustomerMembers",
                column: "BaleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails when a member's Bale id is not a whole number (text ids cannot be represented as bigint).
            migrationBuilder.DropIndex(
                name: "IX_CustomerMembers_BaleId",
                table: "CustomerMembers");

            if (IsSqlServer)
            {
                migrationBuilder.AlterColumn<long>(
                    name: "BaleId",
                    table: "CustomerMembers",
                    nullable: false,
                    oldClrType: typeof(string),
                    oldMaxLength: 64);
            }
            else
            {
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" ADD COLUMN \"BaleIdNumber\" INTEGER NOT NULL DEFAULT 0");
                migrationBuilder.Sql("UPDATE \"CustomerMembers\" SET \"BaleIdNumber\" = CAST(\"BaleId\" AS INTEGER)");
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" DROP COLUMN \"BaleId\"");
                migrationBuilder.Sql("ALTER TABLE \"CustomerMembers\" RENAME COLUMN \"BaleIdNumber\" TO \"BaleId\"");
            }

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMembers_BaleId",
                table: "CustomerMembers",
                column: "BaleId",
                unique: true);
        }
    }
}
