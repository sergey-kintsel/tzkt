﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tzkt.Data.Migrations
{
    /// <inheritdoc />
    public partial class TokenValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Value",
                table: "Tokens",
                type: "numeric",
                defaultValue: "0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Value",
                table: "Tokens");
        }
    }
}
