using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Reconstruccion.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class RegistrosTransaccionales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auditoria_operaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Accion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TipoObjeto = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ObjetoId = table.Column<Guid>(type: "uuid", nullable: false),
                    DesastreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detalle = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auditoria_operaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "eventos_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Secuencia = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DesastreId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgregadoId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionAgregado = table.Column<long>(type: "bigint", nullable: false),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TipoEvento = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContenidoJson = table.Column<string>(type: "jsonb", nullable: false),
                    PublicadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Intentos = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UltimoError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventos_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "solicitudes_procesadas",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoComando = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HuellaContenido = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RespuestaSerializada = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solicitudes_procesadas", x => x.RequestId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auditoria_operaciones_DesastreId_Fecha",
                table: "auditoria_operaciones",
                columns: new[] { "DesastreId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_auditoria_operaciones_ObjetoId_Fecha",
                table: "auditoria_operaciones",
                columns: new[] { "ObjetoId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_auditoria_operaciones_RequestId",
                table: "auditoria_operaciones",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_eventos_outbox_DesastreId_VersionAgregado_Secuencia",
                table: "eventos_outbox",
                columns: new[] { "DesastreId", "VersionAgregado", "Secuencia" });

            migrationBuilder.CreateIndex(
                name: "IX_eventos_outbox_PublicadoEn_DesastreId_VersionAgregado_Secue~",
                table: "eventos_outbox",
                columns: new[] { "PublicadoEn", "DesastreId", "VersionAgregado", "Secuencia" });

            migrationBuilder.CreateIndex(
                name: "IX_eventos_outbox_Secuencia",
                table: "eventos_outbox",
                column: "Secuencia",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria_operaciones");

            migrationBuilder.DropTable(
                name: "eventos_outbox");

            migrationBuilder.DropTable(
                name: "solicitudes_procesadas");
        }
    }
}
