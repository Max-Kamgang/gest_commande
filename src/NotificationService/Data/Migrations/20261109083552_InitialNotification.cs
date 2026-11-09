using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCommandes.NotificationService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Recipient = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DeliveryLog = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SendAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SentAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ReadAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EventId",
                table: "Notifications",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EventType_ReceivedAt",
                table: "Notifications",
                columns: new[] { "EventType", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ReceivedAt",
                table: "Notifications",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Severity",
                table: "Notifications",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status",
                table: "Notifications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Subject",
                table: "Notifications",
                column: "Subject");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
