using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiloSupport.Persistence.Migrations
{
    /// <summary>
    /// Adds the <c>SupportPersons</c> table (our own team, shared by all customers, unique Bale user name) and the
    /// optional <c>Customers.BaleChatId</c> (the Bale chat where a customer's people write) with a unique index that
    /// only covers non-NULL values. Hand-authored (no Designer file) like the older migrations; provider neutral.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261115100000_SupportPersonsAndCustomerChat")]
    public partial class SupportPersonsAndCustomerChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportPersons",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(maxLength: 32, nullable: false),
                    BaleUsername = table.Column<string>(maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportPersons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportPersons_BaleUsername",
                table: "SupportPersons",
                column: "BaleUsername",
                unique: true);

            migrationBuilder.AddColumn<long>(
                name: "BaleChatId",
                table: "Customers",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BaleChatId",
                table: "Customers",
                column: "BaleChatId",
                unique: true,
                filter: "[BaleChatId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_BaleChatId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BaleChatId",
                table: "Customers");

            migrationBuilder.DropTable(
                name: "SupportPersons");
        }
    }
}
