using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlShortener.Api.Data.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShortenedLinks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 7, nullable: false),
                OriginalUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                ClickCount = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                ManagementKeyHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShortenedLinks", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShortenedLinks_Code",
            table: "ShortenedLinks",
            column: "Code",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ShortenedLinks");
    }
}

