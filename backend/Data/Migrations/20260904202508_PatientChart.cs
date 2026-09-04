using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AiVoicePortal.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PatientChart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Allergies",
                table: "Patients",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BloodGroup",
                table: "Patients",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmergencyName",
                table: "Patients",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmergencyPhone",
                table: "Patients",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "Patients",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Uhid",
                table: "Patients",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AppointmentId",
                table: "PatientDocuments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PatientCharges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    ChargeDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientCharges_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PatientCharges_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_Uhid",
                table: "Patients",
                column: "Uhid");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocuments_AppointmentId",
                table: "PatientDocuments",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCharges_AppointmentId",
                table: "PatientCharges",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCharges_PatientId",
                table: "PatientCharges",
                column: "PatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_PatientDocuments_Appointments_AppointmentId",
                table: "PatientDocuments",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PatientDocuments_Appointments_AppointmentId",
                table: "PatientDocuments");

            migrationBuilder.DropTable(
                name: "PatientCharges");

            migrationBuilder.DropIndex(
                name: "IX_Patients_Uhid",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_PatientDocuments_AppointmentId",
                table: "PatientDocuments");

            migrationBuilder.DropColumn(
                name: "Allergies",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "BloodGroup",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "EmergencyName",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "EmergencyPhone",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Uhid",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "PatientDocuments");
        }
    }
}
