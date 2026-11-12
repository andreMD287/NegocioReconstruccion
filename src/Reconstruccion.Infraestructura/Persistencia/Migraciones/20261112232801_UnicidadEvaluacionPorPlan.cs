using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reconstruccion.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class UnicidadEvaluacionPorPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_necesidades_PlanId_evaluacion_id",
                table: "necesidades",
                columns: new[] { "PlanId", "evaluacion_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_necesidades_PlanId_evaluacion_id",
                table: "necesidades");
        }
    }
}
