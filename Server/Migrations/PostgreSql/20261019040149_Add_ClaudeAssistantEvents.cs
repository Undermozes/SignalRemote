using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class Add_ClaudeAssistantEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaudeAssistantEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeviceName = table.Column<string>(type: "text", nullable: true),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    Shell = table.Column<string>(type: "text", nullable: true),
                    Purpose = table.Column<string>(type: "text", nullable: true),
                    IsReadOnly = table.Column<bool>(type: "boolean", nullable: true),
                    Output = table.Column<string>(type: "text", nullable: true),
                    DecidedBy = table.Column<string>(type: "text", nullable: true),
                    Screenshot = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaudeAssistantEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaudeAssistantEvents_DeviceId",
                table: "ClaudeAssistantEvents",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaudeAssistantEvents_OrganizationId_Timestamp",
                table: "ClaudeAssistantEvents",
                columns: new[] { "OrganizationId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaudeAssistantEvents_SessionId",
                table: "ClaudeAssistantEvents",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaudeAssistantEvents");
        }
    }
}
