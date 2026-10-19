using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.Sqlite
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganizationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: true),
                    Shell = table.Column<string>(type: "TEXT", nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", nullable: true),
                    IsReadOnly = table.Column<bool>(type: "INTEGER", nullable: true),
                    Output = table.Column<string>(type: "TEXT", nullable: true),
                    DecidedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Screenshot = table.Column<byte[]>(type: "BLOB", nullable: true)
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
