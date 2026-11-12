using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Reconstruccion.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class InicialReconstruccion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planes_reconstruccion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DesastreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CicloAceptado = table.Column<int>(type: "integer", nullable: true),
                    SenalFinVigente = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planes_reconstruccion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "proyectos_reconstruccion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DesastreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FormuladorId = table.Column<Guid>(type: "uuid", nullable: false),
                    responsable_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsable_organismo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contratista_identificacion = table.Column<string>(type: "text", nullable: true),
                    contratista_nombre = table.Column<string>(type: "text", nullable: true),
                    Presupuesto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VersionPresupuesto = table.Column<int>(type: "integer", nullable: false),
                    FechaInicio = table.Column<DateOnly>(type: "date", nullable: true),
                    FechaFin = table.Column<DateOnly>(type: "date", nullable: true),
                    UltimoMotivo = table.Column<string>(type: "text", nullable: true),
                    necesidad_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proyectos_reconstruccion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "alertas_plan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ObjetoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    FechaGeneracion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FechaResolucion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Justificacion = table.Column<string>(type: "text", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alertas_plan", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alertas_plan_planes_reconstruccion_PlanId",
                        column: x => x.PlanId,
                        principalTable: "planes_reconstruccion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "necesidades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_evaluacion = table.Column<long>(type: "bigint", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_activo = table.Column<string>(type: "text", nullable: false),
                    nivel_dano = table.Column<string>(type: "text", nullable: false),
                    fecha_validacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ubicacion = table.Column<string>(type: "jsonb", nullable: false),
                    CicloRecepcion = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProyectoId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvaluacionRevocada = table.Column<bool>(type: "boolean", nullable: false),
                    UltimaJustificacion = table.Column<string>(type: "text", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_necesidades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_necesidades_planes_reconstruccion_PlanId",
                        column: x => x.PlanId,
                        principalTable: "planes_reconstruccion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alertas_proyecto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ObjetoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    FechaGeneracion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FechaResolucion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Justificacion = table.Column<string>(type: "text", nullable: true),
                    ProyectoId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alertas_proyecto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alertas_proyecto_proyectos_reconstruccion_ProyectoId",
                        column: x => x.ProyectoId,
                        principalTable: "proyectos_reconstruccion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "aportes_proyecto",
                columns: table => new
                {
                    ProyectoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fuente = table.Column<string>(type: "text", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aportes_proyecto", x => new { x.ProyectoId, x.Id });
                    table.ForeignKey(
                        name: "FK_aportes_proyecto_proyectos_reconstruccion_ProyectoId",
                        column: x => x.ProyectoId,
                        principalTable: "proyectos_reconstruccion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "avances_proyecto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HitoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Porcentaje = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    PorcentajeAnterior = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    FechaEfectiva = table.Column<DateOnly>(type: "date", nullable: false),
                    FechaRegistro = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MotivoCorreccion = table.Column<string>(type: "text", nullable: true),
                    ProyectoId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avances_proyecto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_avances_proyecto_proyectos_reconstruccion_ProyectoId",
                        column: x => x.ProyectoId,
                        principalTable: "proyectos_reconstruccion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cambios_presupuestales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PresupuestoAnterior = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PresupuestoNuevo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VersionPresupuestoBase = table.Column<int>(type: "integer", nullable: false),
                    SolicitanteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolutorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Motivo = table.Column<string>(type: "text", nullable: false),
                    MotivoResolucion = table.Column<string>(type: "text", nullable: true),
                    FechaSolicitud = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FechaResolucion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProyectoId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cambios_presupuestales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cambios_presupuestales_proyectos_reconstruccion_ProyectoId",
                        column: x => x.ProyectoId,
                        principalTable: "proyectos_reconstruccion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "costos_proyecto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Concepto = table.Column<string>(type: "text", nullable: false),
                    FechaEfectiva = table.Column<DateOnly>(type: "date", nullable: false),
                    FechaRegistro = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CostoRevertidoId = table.Column<Guid>(type: "uuid", nullable: true),
                    MotivoReverso = table.Column<string>(type: "text", nullable: true),
                    ProyectoId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_costos_proyecto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_costos_proyecto_proyectos_reconstruccion_ProyectoId",
                        column: x => x.ProyectoId,
                        principalTable: "proyectos_reconstruccion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documentos_proyecto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Ubicacion = table.Column<string>(type: "text", nullable: false),
                    FechaRegistro = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProyectoId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documentos_proyecto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_documentos_proyecto_proyectos_reconstruccion_ProyectoId",
                        column: x => x.ProyectoId,
                        principalTable: "proyectos_reconstruccion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hitos_proyecto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    FechaInicio = table.Column<DateOnly>(type: "date", nullable: false),
                    FechaFin = table.Column<DateOnly>(type: "date", nullable: false),
                    Peso = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Avance = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    ProyectoId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hitos_proyecto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_hitos_proyecto_proyectos_reconstruccion_ProyectoId",
                        column: x => x.ProyectoId,
                        principalTable: "proyectos_reconstruccion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidencias_avance",
                columns: table => new
                {
                    AvanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Identificador = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Ubicacion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidencias_avance", x => new { x.AvanceId, x.Id });
                    table.ForeignKey(
                        name: "FK_evidencias_avance_avances_proyecto_AvanceId",
                        column: x => x.AvanceId,
                        principalTable: "avances_proyecto",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "aportes_cambio_presupuestal",
                columns: table => new
                {
                    CambioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fuente = table.Column<string>(type: "text", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aportes_cambio_presupuestal", x => new { x.CambioId, x.Id });
                    table.ForeignKey(
                        name: "FK_aportes_cambio_presupuestal_cambios_presupuestales_CambioId",
                        column: x => x.CambioId,
                        principalTable: "cambios_presupuestales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alertas_plan_PlanId",
                table: "alertas_plan",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_alertas_proyecto_ProyectoId",
                table: "alertas_proyecto",
                column: "ProyectoId");

            migrationBuilder.CreateIndex(
                name: "IX_avances_proyecto_ProyectoId",
                table: "avances_proyecto",
                column: "ProyectoId");

            migrationBuilder.CreateIndex(
                name: "IX_cambios_presupuestales_ProyectoId",
                table: "cambios_presupuestales",
                column: "ProyectoId");

            migrationBuilder.CreateIndex(
                name: "IX_costos_proyecto_ProyectoId",
                table: "costos_proyecto",
                column: "ProyectoId");

            migrationBuilder.CreateIndex(
                name: "IX_documentos_proyecto_ProyectoId",
                table: "documentos_proyecto",
                column: "ProyectoId");

            migrationBuilder.CreateIndex(
                name: "IX_hitos_proyecto_ProyectoId",
                table: "hitos_proyecto",
                column: "ProyectoId");

            migrationBuilder.CreateIndex(
                name: "IX_necesidades_PlanId",
                table: "necesidades",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_planes_reconstruccion_DesastreId",
                table: "planes_reconstruccion",
                column: "DesastreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proyectos_reconstruccion_DesastreId",
                table: "proyectos_reconstruccion",
                column: "DesastreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alertas_plan");

            migrationBuilder.DropTable(
                name: "alertas_proyecto");

            migrationBuilder.DropTable(
                name: "aportes_cambio_presupuestal");

            migrationBuilder.DropTable(
                name: "aportes_proyecto");

            migrationBuilder.DropTable(
                name: "costos_proyecto");

            migrationBuilder.DropTable(
                name: "documentos_proyecto");

            migrationBuilder.DropTable(
                name: "evidencias_avance");

            migrationBuilder.DropTable(
                name: "hitos_proyecto");

            migrationBuilder.DropTable(
                name: "necesidades");

            migrationBuilder.DropTable(
                name: "cambios_presupuestales");

            migrationBuilder.DropTable(
                name: "avances_proyecto");

            migrationBuilder.DropTable(
                name: "planes_reconstruccion");

            migrationBuilder.DropTable(
                name: "proyectos_reconstruccion");
        }
    }
}
