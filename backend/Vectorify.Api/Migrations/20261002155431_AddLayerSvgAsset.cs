using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vectorify.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLayerSvgAsset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SvgAssetId",
                table: "layers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_layers_SvgAssetId",
                table: "layers",
                column: "SvgAssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_layers_assets_SvgAssetId",
                table: "layers",
                column: "SvgAssetId",
                principalTable: "assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_layers_assets_SvgAssetId",
                table: "layers");

            migrationBuilder.DropIndex(
                name: "IX_layers_SvgAssetId",
                table: "layers");

            migrationBuilder.DropColumn(
                name: "SvgAssetId",
                table: "layers");
        }
    }
}
