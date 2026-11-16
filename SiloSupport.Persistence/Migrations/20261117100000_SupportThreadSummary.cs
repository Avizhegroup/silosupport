using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiloSupport.Persistence.Migrations
{
    /// <summary>
    /// <c>SupportThreads.Summary</c> (nullable, 1000 characters): the AI's rolling summary of an issue. It is what the AI
    /// reads about the customer's open issues, so a message that returns to an old subject can rejoin its thread however
    /// long ago the thread started. Existing threads keep NULL (the panel and the AI fall back to the title and the
    /// newest messages). Hand-authored like the older migrations; works on SQL Server and SQLite.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117100000_SupportThreadSummary")]
    public partial class SupportThreadSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "SupportThreads",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Summary",
                table: "SupportThreads");
        }
    }
}
