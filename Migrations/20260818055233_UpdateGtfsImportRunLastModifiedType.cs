using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ulasim_veri_servisi.Migrations
{
    /// <inheritdoc />
    public partial class UpdateGtfsImportRunLastModifiedType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""GtfsImportRuns"" ALTER COLUMN ""LastModified"" TYPE timestamp with time zone USING ""LastModified""::timestamp with time zone;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""GtfsImportRuns"" ALTER COLUMN ""LastModified"" TYPE text USING ""LastModified""::text;");
        }
    }
}
