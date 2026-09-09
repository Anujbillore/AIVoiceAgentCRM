using AiVoicePortal.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiVoicePortal.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260906180000_SarvamManagedCalls")]
public class SarvamManagedCalls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExternalId",
            table: "CallLogs",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_CallLogs_ExternalId",
            table: "CallLogs",
            column: "ExternalId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CallLogs_ExternalId",
            table: "CallLogs");

        migrationBuilder.DropColumn(
            name: "ExternalId",
            table: "CallLogs");
    }
}
