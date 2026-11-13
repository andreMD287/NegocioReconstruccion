using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reconstruccion.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class OrdenOutboxPorDesastre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_eventos_outbox_DesastreId_VersionAgregado_Secuencia",
                table: "eventos_outbox");

            migrationBuilder.DropIndex(
                name: "IX_eventos_outbox_PublicadoEn_DesastreId_VersionAgregado_Secue~",
                table: "eventos_outbox");

            migrationBuilder.CreateIndex(
                name: "IX_eventos_outbox_DesastreId_Secuencia",
                table: "eventos_outbox",
                columns: new[] { "DesastreId", "Secuencia" });

            migrationBuilder.CreateIndex(
                name: "IX_eventos_outbox_PublicadoEn_DesastreId_Secuencia",
                table: "eventos_outbox",
                columns: new[] { "PublicadoEn", "DesastreId", "Secuencia" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_eventos_outbox_DesastreId_Secuencia",
                table: "eventos_outbox");

            migrationBuilder.DropIndex(
                name: "IX_eventos_outbox_PublicadoEn_DesastreId_Secuencia",
                table: "eventos_outbox");

            migrationBuilder.CreateIndex(
                name: "IX_eventos_outbox_DesastreId_VersionAgregado_Secuencia",
                table: "eventos_outbox",
                columns: new[] { "DesastreId", "VersionAgregado", "Secuencia" });

            migrationBuilder.CreateIndex(
                name: "IX_eventos_outbox_PublicadoEn_DesastreId_VersionAgregado_Secue~",
                table: "eventos_outbox",
                columns: new[] { "PublicadoEn", "DesastreId", "VersionAgregado", "Secuencia" });
        }
    }
}
