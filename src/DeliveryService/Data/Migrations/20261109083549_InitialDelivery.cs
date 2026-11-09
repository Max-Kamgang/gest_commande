using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCommandes.DeliveryService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Couriers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Vehicle = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Zone = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    MaxConcurrentDeliveries = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Couriers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliverySequences",
                columns: table => new
                {
                    Year = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastValue = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliverySequences", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "Deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CustomerPhone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ShippingAddress = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ShippingCity = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CourierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ReceivedBy = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderNotified = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    AssignedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    PickedUpAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DeliveredAt = table.Column<long>(type: "INTEGER", nullable: true),
                    FailedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CancelledAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RowVersion = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deliveries_Couriers_CourierId",
                        column: x => x.CourierId,
                        principalTable: "Couriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeliveryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ProductName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryLines_Deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "Deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTracking",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeliveryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ChangedBy = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ChangedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryTracking", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryTracking_Deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "Deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Couriers_Code",
                table: "Couriers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Couriers_Email",
                table: "Couriers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_CourierId_Status",
                table: "Deliveries",
                columns: new[] { "CourierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_OrderNumber",
                table: "Deliveries",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_Reference",
                table: "Deliveries",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_Status",
                table: "Deliveries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryLines_DeliveryId_ProductId",
                table: "DeliveryLines",
                columns: new[] { "DeliveryId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTracking_DeliveryId_ChangedAt",
                table: "DeliveryTracking",
                columns: new[] { "DeliveryId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryLines");

            migrationBuilder.DropTable(
                name: "DeliverySequences");

            migrationBuilder.DropTable(
                name: "DeliveryTracking");

            migrationBuilder.DropTable(
                name: "Deliveries");

            migrationBuilder.DropTable(
                name: "Couriers");
        }
    }
}
