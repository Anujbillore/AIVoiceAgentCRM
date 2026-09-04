using System;
using AiVoicePortal.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AiVoicePortal.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260905043000_VoiceCallFlow")]
public class VoiceCallFlow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ConsentMessage",
            table: "AiSettings",
            type: "text",
            nullable: false,
            defaultValue: "This call may be recorded and handled by our clinic AI. You can ask for a person at any time.");

        migrationBuilder.AddColumn<string>(
            name: "TransferNumber",
            table: "AiSettings",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<bool>(
            name: "IsVip",
            table: "Patients",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "Outcome",
            table: "CallLogs",
            type: "text",
            nullable: false,
            defaultValue: "Contained");

        migrationBuilder.AddColumn<string>(
            name: "EscalationReason",
            table: "CallLogs",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "TransferType",
            table: "CallLogs",
            type: "text",
            nullable: false,
            defaultValue: "None");

        migrationBuilder.AddColumn<double>(
            name: "Confidence",
            table: "CallLogs",
            type: "double precision",
            nullable: false,
            defaultValue: 1.0);

        migrationBuilder.AddColumn<string>(
            name: "Sentiment",
            table: "CallLogs",
            type: "text",
            nullable: false,
            defaultValue: "Neutral");

        migrationBuilder.AddColumn<bool>(
            name: "ConsentGiven",
            table: "CallLogs",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<int>(
            name: "PatientId",
            table: "CallLogs",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CallCallbacks",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                CallerName = table.Column<string>(type: "text", nullable: false),
                CallerPhone = table.Column<string>(type: "text", nullable: false),
                Reason = table.Column<string>(type: "text", nullable: false),
                Summary = table.Column<string>(type: "text", nullable: false),
                PreferredTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                Status = table.Column<string>(type: "text", nullable: false),
                Priority = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CallCallbacks", x => x.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CallCallbacks");
        migrationBuilder.DropColumn(name: "ConsentMessage", table: "AiSettings");
        migrationBuilder.DropColumn(name: "TransferNumber", table: "AiSettings");
        migrationBuilder.DropColumn(name: "IsVip", table: "Patients");
        migrationBuilder.DropColumn(name: "Outcome", table: "CallLogs");
        migrationBuilder.DropColumn(name: "EscalationReason", table: "CallLogs");
        migrationBuilder.DropColumn(name: "TransferType", table: "CallLogs");
        migrationBuilder.DropColumn(name: "Confidence", table: "CallLogs");
        migrationBuilder.DropColumn(name: "Sentiment", table: "CallLogs");
        migrationBuilder.DropColumn(name: "ConsentGiven", table: "CallLogs");
        migrationBuilder.DropColumn(name: "PatientId", table: "CallLogs");
    }
}
