using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DailyGate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionDeviceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientVersion",
                table: "Submissions",
                type: "text",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                table: "Submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceName",
                table: "Submissions",
                type: "text",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "ServiceVersion",
                table: "Submissions",
                type: "text",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.Sql("""
                UPDATE "Submissions" AS s
                SET "DeviceId" = d."Id",
                    "DeviceName" = d."Name",
                    "ClientVersion" = d."ClientVersion",
                    "ServiceVersion" = d."ServiceVersion"
                FROM "Devices" AS d
                WHERE d."EmployeeId" = s."EmployeeId";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "DeviceId",
                table: "Submissions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_DeviceId",
                table: "Submissions",
                column: "DeviceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Submissions_Devices_DeviceId",
                table: "Submissions",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Submissions_Devices_DeviceId",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Submissions_DeviceId",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "ClientVersion",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "ServiceVersion",
                table: "Submissions");
        }
    }
}
