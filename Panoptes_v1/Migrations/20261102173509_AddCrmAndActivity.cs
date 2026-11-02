using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panoptes_v1.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmAndActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "ActivityEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Verb = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Ref = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TaxId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TaxIdNormalized = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TaxOffice = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    Addresses = table.Column<string>(type: "jsonb", nullable: false),
                    ContactMethods = table.Column<string>(type: "jsonb", nullable: false),
                    MatchEmails = table.Column<List<string>>(type: "text[]", nullable: false),
                    MatchPhones = table.Column<List<string>>(type: "text[]", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContactMethods = table.Column<string>(type: "jsonb", nullable: false),
                    MatchEmails = table.Column<List<string>>(type: "text[]", nullable: false),
                    MatchPhones = table.Column<List<string>>(type: "text[]", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    IsBillingContact = table.Column<bool>(type: "boolean", nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    EndedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientContacts_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientContacts_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_ClientId_OccurredAt",
                table: "ActivityEvents",
                columns: new[] { "ClientId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_EntityType_EntityId_OccurredAt",
                table: "ActivityEvents",
                columns: new[] { "EntityType", "EntityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_ProjectId",
                table: "ActivityEvents",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_ClientId_PersonId",
                table: "ClientContacts",
                columns: new[] { "ClientId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_ClientId_Primary",
                table: "ClientContacts",
                column: "ClientId",
                unique: true,
                filter: "\"IsPrimary\"");

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_PersonId",
                table: "ClientContacts",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_DisplayName",
                table: "Clients",
                column: "DisplayName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_LegalName",
                table: "Clients",
                column: "LegalName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_MatchEmails",
                table: "Clients",
                column: "MatchEmails")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_MatchPhones",
                table: "Clients",
                column: "MatchPhones")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Ref",
                table: "Clients",
                column: "Ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Tags",
                table: "Clients",
                column: "Tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_TaxIdNormalized",
                table: "Clients",
                column: "TaxIdNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_People_FirstName",
                table: "People",
                column: "FirstName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_People_LastName",
                table: "People",
                column: "LastName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_People_MatchEmails",
                table: "People",
                column: "MatchEmails")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_People_MatchPhones",
                table: "People",
                column: "MatchPhones")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvents");

            migrationBuilder.DropTable(
                name: "ClientContacts");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
