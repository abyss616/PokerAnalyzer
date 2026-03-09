using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokerAnalyzer.Infrastructure.Persistence.Migrations;

public partial class AddHandActionSequenceNumber : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SequenceNumber",
            table: "HandActions",
            type: "INTEGER",
            nullable: false,
            defaultValue: -1);

        migrationBuilder.Sql(@"
WITH Ordered AS (
    SELECT Id,
           HandId,
           ROW_NUMBER() OVER (PARTITION BY HandId ORDER BY rowid, Id) - 1 AS Seq
    FROM HandActions
)
UPDATE HandActions
SET SequenceNumber = (
    SELECT Seq
    FROM Ordered
    WHERE Ordered.Id = HandActions.Id
      AND Ordered.HandId = HandActions.HandId
)
WHERE SequenceNumber = -1;");

        migrationBuilder.CreateIndex(
            name: "IX_HandActions_HandId_SequenceNumber",
            table: "HandActions",
            columns: new[] { "HandId", "SequenceNumber" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_HandActions_HandId_SequenceNumber",
            table: "HandActions");

        migrationBuilder.DropColumn(
            name: "SequenceNumber",
            table: "HandActions");
    }
}
