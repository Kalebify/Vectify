using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vectorify.Api.Migrations
{
    /// <inheritdoc />
    public partial class MoveDocumentDimensionsToVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeightMm",
                table: "vector_documents");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "vector_documents");

            migrationBuilder.DropColumn(
                name: "ViewBox",
                table: "vector_documents");

            migrationBuilder.DropColumn(
                name: "WidthMm",
                table: "vector_documents");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "layers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<double>(
                name: "HeightMm",
                table: "document_versions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "document_versions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ViewBox",
                table: "document_versions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "WidthMm",
                table: "document_versions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateIndex(
                name: "IX_layers_VersionId_GroupId",
                table: "layers",
                columns: new[] { "VersionId", "GroupId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_layers_VersionId_GroupId",
                table: "layers");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "layers");

            migrationBuilder.DropColumn(
                name: "HeightMm",
                table: "document_versions");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "document_versions");

            migrationBuilder.DropColumn(
                name: "ViewBox",
                table: "document_versions");

            migrationBuilder.DropColumn(
                name: "WidthMm",
                table: "document_versions");

            migrationBuilder.AddColumn<double>(
                name: "HeightMm",
                table: "vector_documents",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "vector_documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ViewBox",
                table: "vector_documents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "WidthMm",
                table: "vector_documents",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
