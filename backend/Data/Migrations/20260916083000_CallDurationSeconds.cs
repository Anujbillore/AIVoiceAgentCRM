using AiVoicePortal.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiVoicePortal.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260916083000_CallDurationSeconds")]
public class CallDurationSeconds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "DurationSeconds",
            table: "CallLogs",
            type: "double precision",
            nullable: false,
            defaultValue: 0.0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DurationSeconds",
            table: "CallLogs");
    }
}
