using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Telemetry.Api.Migrations
{
    /// <inheritdoc />
    public partial class add_composite_index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Telemetry",
                table: "Telemetry");

            migrationBuilder.RenameTable(
                name: "Telemetry",
                newName: "TelemetryEvent");

            migrationBuilder.RenameIndex(
                name: "IX_Telemetry_Source_Ts",
                table: "TelemetryEvent",
                newName: "IX_Telemetry_Source_Timestamp");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TelemetryEvent",
                table: "TelemetryEvent",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TelemetryEvent",
                table: "TelemetryEvent");

            migrationBuilder.RenameTable(
                name: "TelemetryEvent",
                newName: "Telemetry");

            migrationBuilder.RenameIndex(
                name: "IX_Telemetry_Source_Timestamp",
                table: "Telemetry",
                newName: "IX_Telemetry_Source_Ts");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Telemetry",
                table: "Telemetry",
                column: "Id");
        }
    }
}
