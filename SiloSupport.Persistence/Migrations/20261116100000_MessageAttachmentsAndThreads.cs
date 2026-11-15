using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiloSupport.Persistence.Migrations
{
    /// <summary>
    /// Customer files and issue threads:
    /// <list type="bullet">
    /// <item><c>SupportThreads</c> - one row per issue of a customer (title, creation and last activity time).</item>
    /// <item><c>CustomerGroupedMessages.ThreadId</c> (nullable, ON DELETE SET NULL) and <c>OriginalText</c> (the text the
    /// customer wrote, used as context for the AI); existing messages keep NULL in both.</item>
    /// <item><c>MessageAttachments</c> - the photo/video/voice/audio/document/animation sent with a message, one per message,
    /// the bytes in <c>Content</c> (varbinary(max)).</item>
    /// </list>
    /// Hand-authored (no Designer file) like the older migrations. SQLite cannot add a foreign key to an existing table, so
    /// the test provider adds the column with an inline REFERENCES clause instead; its <c>Down</c> is not supported.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261116100000_MessageAttachmentsAndThreads")]
    public partial class MessageAttachmentsAndThreads : Migration
    {
        private bool IsSqlServer => ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportThreads",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerId = table.Column<int>(nullable: false),
                    Title = table.Column<string>(maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(nullable: false),
                    LastActivityUtc = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportThreads_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportThreads_CustomerId_LastActivityUtc",
                table: "SupportThreads",
                columns: new[] { "CustomerId", "LastActivityUtc" });

            migrationBuilder.AddColumn<string>(
                name: "OriginalText",
                table: "CustomerGroupedMessages",
                maxLength: 4000,
                nullable: true);

            if (IsSqlServer)
            {
                migrationBuilder.AddColumn<int>(
                    name: "ThreadId",
                    table: "CustomerGroupedMessages",
                    nullable: true);

                migrationBuilder.AddForeignKey(
                    name: "FK_CustomerGroupedMessages_SupportThreads_ThreadId",
                    table: "CustomerGroupedMessages",
                    column: "ThreadId",
                    principalTable: "SupportThreads",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            }
            else
            {
                migrationBuilder.Sql(
                    "ALTER TABLE \"CustomerGroupedMessages\" ADD COLUMN \"ThreadId\" INTEGER NULL " +
                    "REFERENCES \"SupportThreads\" (\"Id\") ON DELETE SET NULL");
            }

            migrationBuilder.CreateIndex(
                name: "IX_CustomerGroupedMessages_ThreadId",
                table: "CustomerGroupedMessages",
                column: "ThreadId");

            migrationBuilder.CreateTable(
                name: "MessageAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupedMessageId = table.Column<int>(nullable: false),
                    Kind = table.Column<string>(maxLength: 16, nullable: false),
                    FileName = table.Column<string>(maxLength: 260, nullable: true),
                    MimeType = table.Column<string>(maxLength: 128, nullable: true),
                    SizeBytes = table.Column<long>(nullable: true),
                    BaleFileId = table.Column<string>(maxLength: 512, nullable: false),
                    BaleFileUniqueId = table.Column<string>(maxLength: 128, nullable: true),
                    Content = table.Column<byte[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageAttachments_CustomerGroupedMessages_GroupedMessageId",
                        column: x => x.GroupedMessageId,
                        principalTable: "CustomerGroupedMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_GroupedMessageId",
                table: "MessageAttachments",
                column: "GroupedMessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!IsSqlServer)
                throw new NotSupportedException("Reverting this migration is only supported on SQL Server (SQLite cannot drop a foreign-key column).");

            migrationBuilder.DropTable(
                name: "MessageAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerGroupedMessages_SupportThreads_ThreadId",
                table: "CustomerGroupedMessages");

            migrationBuilder.DropIndex(
                name: "IX_CustomerGroupedMessages_ThreadId",
                table: "CustomerGroupedMessages");

            migrationBuilder.DropColumn(
                name: "ThreadId",
                table: "CustomerGroupedMessages");

            migrationBuilder.DropColumn(
                name: "OriginalText",
                table: "CustomerGroupedMessages");

            migrationBuilder.DropTable(
                name: "SupportThreads");
        }
    }
}
