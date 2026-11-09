using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCommandes.InventoryService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReservationLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReservationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationLines_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductLabel = table.Column<string>(type: "TEXT", maxLength: 180, nullable: true),
                    WarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityOnHand = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityReserved = table.Column<int>(type: "INTEGER", nullable: false),
                    LowStockThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    RowVersion = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItems", x => x.Id);
                    table.CheckConstraint("CK_StockItems_OnHand_Positif", "QuantityOnHand >= 0");
                    table.CheckConstraint("CK_StockItems_Reserved_Max", "QuantityReserved <= QuantityOnHand");
                    table.CheckConstraint("CK_StockItems_Reserved_Positif", "QuantityReserved >= 0");
                    table.ForeignKey(
                        name: "FK_StockItems_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StockItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityOnHandAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityReservedAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    PerformedBy = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalTable: "StockItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLines_ReservationId_ProductId_WarehouseId",
                table: "ReservationLines",
                columns: new[] { "ReservationId", "ProductId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Reference",
                table: "Reservations",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status",
                table: "Reservations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_ProductId",
                table: "StockItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_ProductId_WarehouseId",
                table: "StockItems",
                columns: new[] { "ProductId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_WarehouseId",
                table: "StockItems",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OccurredAt",
                table: "StockMovements",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId_OccurredAt",
                table: "StockMovements",
                columns: new[] { "ProductId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_Reference",
                table: "StockMovements",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StockItemId",
                table: "StockMovements",
                column: "StockItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationLines");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "StockItems");

            migrationBuilder.DropTable(
                name: "Warehouses");
        }
    }
}
