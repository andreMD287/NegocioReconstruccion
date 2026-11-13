using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Reconstruccion.Infraestructura.Persistencia.Repositorios;
using Testcontainers.PostgreSql;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class AtomicidadAuditoriaPostgreSqlEnContenedorTests
    : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:16.13")
            .Build();

    private DbContextOptions<ReconstruccionDbContext> Opciones =>
        new DbContextOptionsBuilder<ReconstruccionDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = new ReconstruccionDbContext(Opciones);

        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() =>
        _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task PlanDuplicado_NoDejaAuditoriaSolicitudNiOutboxParciales()
    {
        var desastreId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var requestOriginal = Guid.NewGuid();
        var requestFallido = Guid.NewGuid();
        var fecha = DateTimeOffset.UtcNow;

        var plan = PlanReconstruccion.Abrir(
            planId,
            desastreId);

        // Evento sintetico para verificar persistencia.
        // No representa un cambio real del estado del Plan.
        var eventoOriginal = new PlanCompletado(
            desastreId,
            planId,
            plan.Version,
            fecha);

        // PRIMERA OPERACION: guardar conjuntamente los
        // cuatro componentes de la unidad de trabajo.
        await using (var db = new ReconstruccionDbContext(Opciones))
        {
            var fabrica = new FabricaAuditoriaPrueba(Opciones);

            new RepositorioPlanesEf(db).Agregar(plan);

            new RegistroAuditoriaEf(db, fabrica).Registrar(
                new EntradaAuditoria(
                    Guid.NewGuid(),
                    default(Rol),
                    "AltaPlanOriginal",
                    "Plan",
                    planId,
                    desastreId,
                    fecha,
                    ResultadoOperacion.Exitoso,
                    requestOriginal,
                    "Operacion inicial valida"));

            new RegistroSolicitudesEf(db).Registrar(
                new SolicitudProcesada(
                    requestOriginal,
                    "AltaPlanOriginal",
                    new string('A', 64),
                    "{\"estado\":\"confirmado\"}"));

            new OutboxEf(db).Agregar([eventoOriginal]);

            Assert.True(
                db.ChangeTracker.Entries()
                    .Count(x => x.State == EntityState.Added) >= 4);

            await new UnidadDeTrabajoEf(db).ConfirmarAsync();
        }

        // Comprobar persistencia desde otro DbContext.
        await using (var verificar =
            new ReconstruccionDbContext(Opciones))
        {
            Assert.Equal(
                1,
                await verificar.Planes.CountAsync(
                    x => x.Id == planId));

            Assert.Equal(
                1,
                await verificar.Auditorias.CountAsync(
                    x => x.RequestId == requestOriginal));

            Assert.Equal(
                1,
                await verificar.SolicitudesProcesadas.CountAsync(
                    x => x.RequestId == requestOriginal));

            Assert.Equal(
                1,
                await verificar.EventosOutbox.CountAsync(
                    x => x.Id == eventoOriginal.EventoId));
        }

        // SEGUNDA OPERACION: otro Plan con el mismo desastre.
        // Se preparan tambien auditoria, solicitud y Outbox.
        var planDuplicado = PlanReconstruccion.Abrir(
            Guid.NewGuid(),
            desastreId);

        var eventoFallido = new PlanCompletado(
            desastreId,
            planDuplicado.Id,
            planDuplicado.Version,
            fecha);

        await using (var db = new ReconstruccionDbContext(Opciones))
        {
            var fabrica = new FabricaAuditoriaPrueba(Opciones);

            new RepositorioPlanesEf(db).Agregar(planDuplicado);

            new RegistroAuditoriaEf(db, fabrica).Registrar(
                new EntradaAuditoria(
                    Guid.NewGuid(),
                    default(Rol),
                    "IntentoPlanDuplicado",
                    "Plan",
                    planDuplicado.Id,
                    desastreId,
                    fecha,
                    ResultadoOperacion.Exitoso,
                    requestFallido,
                    "No debe persistir"));

            new RegistroSolicitudesEf(db).Registrar(
                new SolicitudProcesada(
                    requestFallido,
                    "AltaPlanDuplicado",
                    new string('B', 64),
                    "{\"estado\":\"no guardar\"}"));

            new OutboxEf(db).Agregar([eventoFallido]);

            var conflicto = await Assert.ThrowsAsync<
                ConflictoDeVersionException>(
                    () => new UnidadDeTrabajoEf(db).ConfirmarAsync());

            var errorEf = Assert.IsType<DbUpdateException>(
                conflicto.InnerException);

            var errorPg = Assert.IsType<PostgresException>(
                errorEf.InnerException);

            Assert.Equal(
                PostgresErrorCodes.UniqueViolation,
                errorPg.SqlState);

            Assert.Equal(
                "IX_planes_reconstruccion_DesastreId",
                errorPg.ConstraintName);
        }

        // TERCERA COMPROBACION: SaveChanges fallo,
        // por lo que ninguna parte del intento debe persistir.
        await using (var verificar =
            new ReconstruccionDbContext(Opciones))
        {
            Assert.Equal(
                1,
                await verificar.Planes.CountAsync(
                    x => x.DesastreId == desastreId));

            Assert.Equal(
                0,
                await verificar.Auditorias.CountAsync(
                    x => x.RequestId == requestFallido));

            Assert.Equal(
                0,
                await verificar.SolicitudesProcesadas.CountAsync(
                    x => x.RequestId == requestFallido));

            Assert.Equal(
                0,
                await verificar.EventosOutbox.CountAsync(
                    x => x.Id == eventoFallido.EventoId));

            // La primera operacion permanece intacta.
            Assert.Equal(
                1,
                await verificar.Auditorias.CountAsync(
                    x => x.RequestId == requestOriginal));

            Assert.Equal(
                1,
                await verificar.SolicitudesProcesadas.CountAsync(
                    x => x.RequestId == requestOriginal));

            Assert.Equal(
                1,
                await verificar.EventosOutbox.CountAsync(
                    x => x.Id == eventoOriginal.EventoId));
        }
    }

    [Fact]
    public async Task AuditoriaRechazada_SobreviveRollbackDeOperacionPrincipal()
    {
        var requestExito = Guid.NewGuid();
        var requestRechazo = Guid.NewGuid();

        var entrada = new EntradaAuditoria(
            Guid.NewGuid(),
            default(Rol),
            "PruebaAuditoriaIndependiente",
            "Proyecto",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            ResultadoOperacion.Exitoso,
            requestExito,
            "Operacion principal");

        var rechazo = entrada with
        {
            Resultado = ResultadoOperacion.Rechazado,
            RequestId = requestRechazo,
            Detalle = "Rechazo registrado independientemente"
        };

        // La fabrica abre contextos independientes:
        // no comparte la transaccion principal.
        var fabrica = new FabricaAuditoriaPrueba(Opciones);

        await using (var principal =
            new ReconstruccionDbContext(Opciones))
        {
            await using var transaccion =
                await principal.Database.BeginTransactionAsync();

            var auditoria = new RegistroAuditoriaEf(
                principal,
                fabrica);

            var solicitudes = new RegistroSolicitudesEf(principal);

            // Dos registros dentro de la transaccion principal.
            auditoria.Registrar(entrada);

            solicitudes.Registrar(
                new SolicitudProcesada(
                    requestExito,
                    "PruebaAuditoriaIndependiente",
                    new string('A', 64),
                    "{\"ok\":true}"));

            await new UnidadDeTrabajoEf(principal).ConfirmarAsync();

            var recuperada =
                await solicitudes.ObtenerAsync(requestExito);

            Assert.NotNull(recuperada);

            Assert.Equal(
                "{\"ok\":true}",
                recuperada?.RespuestaSerializada);

            // Este registro usa otra conexion y se confirma aparte.
            await auditoria.RegistrarRechazoAsync(rechazo);

            // Revertir solamente la operacion principal.
            await transaccion.RollbackAsync();
        }

        // Comprobar desde un tercer contexto independiente.
        await using var verificar =
            new ReconstruccionDbContext(Opciones);

        // La auditoria de exito fue revertida.
        Assert.Equal(
            0,
            await verificar.Auditorias.CountAsync(
                x => x.RequestId == requestExito));

        // La solicitud principal tambien fue revertida.
        Assert.Equal(
            0,
            await verificar.SolicitudesProcesadas.CountAsync(
                x => x.RequestId == requestExito));

        // El rechazo independiente debe sobrevivir.
        Assert.Equal(
            1,
            await verificar.Auditorias.CountAsync(
                x => x.RequestId == requestRechazo));

        var resultado = await verificar.Auditorias
            .Where(x => x.RequestId == requestRechazo)
            .Select(x => x.Resultado)
            .SingleAsync();

        Assert.Equal(
            ResultadoOperacion.Rechazado,
            resultado);
    }

    // Fabrica exclusiva de pruebas. Cada nuevo contexto
    // abre su propia conexion a PostgreSQL.
    private sealed class FabricaAuditoriaPrueba(
        DbContextOptions<ReconstruccionDbContext> opciones)
        : IDbContextFactory<ReconstruccionDbContext>
    {
        public ReconstruccionDbContext CreateDbContext() =>
            new(opciones);

        public Task<ReconstruccionDbContext> CreateDbContextAsync(
            CancellationToken cancelacion = default) =>
            Task.FromResult(CreateDbContext());
    }
}