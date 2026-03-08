using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrigramSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Messages_Body_Trgm\" ON \"Messages\" USING gin (\"Body\" gin_trgm_ops);");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_DirectMessages_Body_Trgm\" ON \"DirectMessages\" USING gin (\"Body\" gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_DirectMessages_Body_Trgm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Messages_Body_Trgm\";");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS pg_trgm;");
        }
    }
}
