using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Reconstruccion.Infraestructura.Persistencia.Repositorios;
using Testcontainers.PostgreSql;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class NecesidadesJsonbPostgreSqlEnContenedorTests
    : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:16.13").Build();

    private DbContextOptions<ReconstruccionDbContext> Opciones =>
        new DbContextOptionsBuilder<ReconstruccionDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db =
            new ReconstruccionDbContext(Opciones);

        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() =>
        _postgres.DisposeAsync().AsTask();

    private static (
        PlanReconstruccion Plan,
        EvaluacionOrigen Origen,
        Guid NecesidadId)
        CrearPlanConNecesidad()
    {
        var ahora = DateTimeOffset.UtcNow;

        var origen = new EvaluacionOrigen(
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            "Edificio",
            "Alto",
            ahora,
            new Ubicacion(
                4.65,
                -74.10,
                "Ubicacion inicial"));

        var plan = PlanReconstruccion.Abrir(
            Guid.NewGuid(),
            Guid.NewGuid());

        plan.RecibirEvaluaciones(
            1,
            [new EvaluacionEntrante(origen, true, true)],
            Guid.NewGuid,
            ahora);

        var necesidad = Assert.Single(plan.Necesidades);

        return (plan, origen, necesidad.Id);
    }

    [Fact]
    public async Task UbicacionJsonb_SeRecuperaYCorrigeConVersionYAlerta()
    {
        var (plan, origen, necesidadId) =
            CrearPlanConNecesidad();

        var planId = plan.Id;
        var versionInicial = plan.Version;

        // 1. Guardar la necesidad y su ubicacion inicial.
        await using (var alta =
            new ReconstruccionDbContext(Opciones))
        {
            new RepositorioPlanesEf(alta).Agregar(plan);

            await new UnidadDeTrabajoEf(alta).ConfirmarAsync();
        }

        // 2. Verificar en PostgreSQL que la columna
        // contiene un objeto JSONB real.
        await using (var conexion =
            new NpgsqlConnection(
                _postgres.GetConnectionString()))
        {
            await conexion.OpenAsync();

            await using var comando = new NpgsqlCommand(
                """
                SELECT jsonb_typeof(ubicacion)
                FROM public.necesidades
                WHERE "Id" = @necesidadId;
                """,
                conexion);

            comando.Parameters.AddWithValue(
                "necesidadId",
                necesidadId);

            var tipoJson = await comando.ExecuteScalarAsync();

            Assert.Equal("object", tipoJson);
        }

        // 3. Recuperar el Plan desde otro contexto.
        await using (var actualizacion =
            new ReconstruccionDbContext(Opciones))
        {
            var planCargado = await actualizacion.Planes
                .Include(p => p.Necesidades)
                .Include(p => p.Alertas)
                .AsSplitQuery()
                .SingleAsync(p => p.Id == planId);

            var necesidad = Assert.Single(
                planCargado.Necesidades);

            Assert.Equal(necesidadId, necesidad.Id);

            Assert.Equal(
                origen.EvaluacionId,
                necesidad.Origen.EvaluacionId);

            Assert.Equal(
                1,
                necesidad.Origen.VersionEvaluacion);

            Assert.Equal(
                new Ubicacion(
                    4.65,
                    -74.10,
                    "Ubicacion inicial"),
                necesidad.Origen.Ubicacion);

            // 4. Corregir la evaluacion desde el dominio.
            var corregido = origen with
            {
                VersionEvaluacion = 2,
                Ubicacion = new Ubicacion(
                    4.66,
                    -74.09,
                    "Ubicacion corregida")
            };

            planCargado.AplicarCambioEvaluacion(
                origen.EvaluacionId,
                2,
                TipoCambioOrigen.Correccion,
                corregido,
                DateTimeOffset.UtcNow);

            await new UnidadDeTrabajoEf(
                actualizacion).ConfirmarAsync();
        }

        // 5. Comprobar materializacion y persistencia
        // de la correccion desde un tercer contexto.
        await using (var verificacion =
            new ReconstruccionDbContext(Opciones))
        {
            var planFinal = await verificacion.Planes
                .Include(p => p.Necesidades)
                .Include(p => p.Alertas)
                .AsSplitQuery()
                .SingleAsync(p => p.Id == planId);

            var necesidadFinal =
                Assert.Single(planFinal.Necesidades);

            Assert.Equal(
                necesidadId,
                necesidadFinal.Id);

            Assert.Equal(
                2,
                necesidadFinal.Origen.VersionEvaluacion);

            Assert.Equal(
                new Ubicacion(
                    4.66,
                    -74.09,
                    "Ubicacion corregida"),
                necesidadFinal.Origen.Ubicacion);

            Assert.Single(planFinal.Alertas);

            Assert.True(
                planFinal.Version > versionInicial);
        }

        // 6. Confirmar nuevamente el tipo JSONB
        // despues de la actualizacion.
        await using (var conexion =
            new NpgsqlConnection(
                _postgres.GetConnectionString()))
        {
            await conexion.OpenAsync();

            await using var comando = new NpgsqlCommand(
                """
                SELECT jsonb_typeof(ubicacion)
                FROM public.necesidades
                WHERE "Id" = @necesidadId;
                """,
                conexion);

            comando.Parameters.AddWithValue(
                "necesidadId",
                necesidadId);

            Assert.Equal(
                "object",
                await comando.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task EvaluacionDuplicada_PostgreSqlRechazaIndiceUnico()
    {
        var (plan, origen, necesidadId) =
            CrearPlanConNecesidad();

        // 1. Guardar una evaluacion valida.
        await using (var alta =
            new ReconstruccionDbContext(Opciones))
        {
            new RepositorioPlanesEf(alta).Agregar(plan);

            await new UnidadDeTrabajoEf(alta).ConfirmarAsync();
        }

        // 2. Intentar duplicar la misma evaluacion
        // del mismo Plan, pero con un Id de fila distinto.
        //
        // Este SQL corresponde al Smoke original.
        // No modifica ninguna tabla si PostgreSQL
        // rechaza la instruccion.
        await using (var conexion =
            new NpgsqlConnection(
                _postgres.GetConnectionString()))
        {
            await conexion.OpenAsync();

            await using var comando = new NpgsqlCommand(
                """
                INSERT INTO public.necesidades (
                    "Id",
                    evaluacion_id,
                    version_evaluacion,
                    activo_id,
                    tipo_activo,
                    nivel_dano,
                    fecha_validacion,
                    ubicacion,
                    "CicloRecepcion",
                    "Estado",
                    "ProyectoId",
                    "EvaluacionRevocada",
                    "UltimaJustificacion",
                    "PlanId"
                )
                SELECT
                    @nuevoId,
                    evaluacion_id,
                    version_evaluacion,
                    activo_id,
                    tipo_activo,
                    nivel_dano,
                    fecha_validacion,
                    ubicacion,
                    "CicloRecepcion",
                    "Estado",
                    "ProyectoId",
                    "EvaluacionRevocada",
                    "UltimaJustificacion",
                    "PlanId"
                FROM public.necesidades
                WHERE "Id" = @originalId;
                """,
                conexion);

            comando.Parameters.AddWithValue(
                "nuevoId",
                Guid.NewGuid());

            comando.Parameters.AddWithValue(
                "originalId",
                necesidadId);

            var error = await Assert.ThrowsAsync<
                PostgresException>(
                    async () =>
                    {
                        await comando.ExecuteNonQueryAsync();
                    });

            Assert.Equal(
                PostgresErrorCodes.UniqueViolation,
                error.SqlState);

            Assert.Equal(
                "UX_necesidades_PlanId_evaluacion_id",
                error.ConstraintName);
        }

        // 3. El rechazo debe conservar la fila original.
        await using (var verificacion =
            new ReconstruccionDbContext(Opciones))
        {
            var planFinal = await verificacion.Planes
                .Include(p => p.Necesidades)
                .AsSplitQuery()
                .SingleAsync(p => p.Id == plan.Id);

            var necesidad =
                Assert.Single(planFinal.Necesidades);

            Assert.Equal(necesidadId, necesidad.Id);

            Assert.Equal(
                origen.EvaluacionId,
                necesidad.Origen.EvaluacionId);

            Assert.Equal(
                1,
                necesidad.Origen.VersionEvaluacion);

            Assert.Equal(
                new Ubicacion(
                    4.65,
                    -74.10,
                    "Ubicacion inicial"),
                necesidad.Origen.Ubicacion);
        }
    }
}