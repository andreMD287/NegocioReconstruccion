using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;

internal static class PruebaAdaptadores
{
    public static async Task EjecutarAsync(
        DbContextOptions<ReconstruccionDbContext> opciones,
        NpgsqlTransaction transaccion)
    {
        Console.WriteLine("Iniciando prueba real de auditoria e idempotencia.");

        var conexionIndependiente =
            Environment.GetEnvironmentVariable(
                "RECONSTRUCCION_DB_CONNECTION");

        if (string.IsNullOrWhiteSpace(conexionIndependiente))
            throw new Exception("Falta la conexion de laboratorio.");

        // La fabrica NO comparte la conexion ni la transaccion principal.
        var opcionesIndependientes =
            new DbContextOptionsBuilder<ReconstruccionDbContext>()
                .UseNpgsql(conexionIndependiente)
                .Options;

        var fabrica = new FabricaIndependientePrueba(
            opcionesIndependientes);

        var requestExito = Guid.NewGuid();
        var requestRechazo = Guid.NewGuid();

        var entrada = new EntradaAuditoria(
            Guid.NewGuid(),
            default(Rol),
            "SmokeAdaptadores",
            "Proyecto",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            ResultadoOperacion.Exitoso,
            requestExito,
            "Operacion de prueba");

        var rechazo = entrada with
        {
            Resultado = ResultadoOperacion.Rechazado,
            RequestId = requestRechazo,
            Detalle = "Rechazo independiente de prueba"
        };

        const string punto = "paso61_adaptadores";
        await transaccion.SaveAsync(punto);

        var revertido = false;

        try
        {
            using (var principal = new ReconstruccionDbContext(opciones))
            {
                await principal.Database.UseTransactionAsync(transaccion);

                var auditoria = new RegistroAuditoriaEf(
                    principal, fabrica);

                var solicitudes = new RegistroSolicitudesEf(principal);

                // 1. Registros que participan en la transaccion.
                auditoria.Registrar(entrada);

                solicitudes.Registrar(new SolicitudProcesada(
                    requestExito,
                    "SmokeAdaptadores",
                    new string('A', 64),
                    "{\"ok\":true}"));

                await principal.SaveChangesAsync();

                var recuperada = await solicitudes.ObtenerAsync(requestExito);

                if (recuperada is null ||
                    recuperada.RespuestaSerializada != "{\"ok\":true}")
                {
                    throw new Exception(
                        "No se recupero correctamente la solicitud.");
                }

                Console.WriteLine(
                    "Auditoria y solicitud principal guardadas provisionalmente.");

                // 2. Este rechazo utiliza una conexion distinta.
                await auditoria.RegistrarRechazoAsync(rechazo);

                Console.WriteLine(
                    "Rechazo guardado en una transaccion independiente.");
            }

            // 3. Revertir solamente los registros principales de prueba.
            await transaccion.RollbackAsync(punto);
            revertido = true;

            // 4. Inspeccionar desde otra conexion real.
            await using (var verificacion =
                new ReconstruccionDbContext(opcionesIndependientes))
            {
                var exitosas = await verificacion.Auditorias
                    .CountAsync(x => x.RequestId == requestExito);

                var rechazos = await verificacion.Auditorias
                    .CountAsync(x => x.RequestId == requestRechazo);

                var solicitudes = await verificacion.SolicitudesProcesadas
                    .CountAsync(x => x.RequestId == requestExito);

                if (exitosas != 0 || solicitudes != 0)
                {
                    throw new Exception(
                        "La transaccion principal dejo registros persistidos.");
                }

                if (rechazos != 1)
                {
                    throw new Exception(
                        "El rechazo independiente no sobrevivio al rollback.");
                }

                var resultadoRechazo = await verificacion.Auditorias
                    .Where(x => x.RequestId == requestRechazo)
                    .Select(x => x.Resultado)
                    .SingleAsync();

                if (resultadoRechazo != ResultadoOperacion.Rechazado)
                {
                    throw new Exception(
                        "La auditoria persistida no indica Rechazado.");
                }
            }

            Console.WriteLine(
                "Rollback principal confirmado: auditoria exitosa y solicitud ausentes.");

            Console.WriteLine(
                "Auditoria rechazada conservada en PostgreSQL.");

            Console.WriteLine("PRUEBA DE ADAPTADORES SUPERADA.");
        }
        finally
        {
            // Restaurar la transaccion de laboratorio.
            if (!revertido)
                await transaccion.RollbackAsync(punto);

            await transaccion.ReleaseAsync(punto);

            // Limpieza EXCLUSIVA de la fila creada por esta prueba.
            // Nunca se utiliza esta eliminacion en el adaptador productivo.
            await using var limpieza =
                new ReconstruccionDbContext(opcionesIndependientes);

            await limpieza.Auditorias
                .Where(x => x.RequestId == requestRechazo)
                .ExecuteDeleteAsync();

            Console.WriteLine(
                "Registro independiente de laboratorio limpiado.");
        }
    }
}

// Fabrica minima exclusiva de la prueba.
// Cada contexto nuevo utiliza una conexion propia a PostgreSQL.
internal sealed class FabricaIndependientePrueba(
    DbContextOptions<ReconstruccionDbContext> opciones)
    : IDbContextFactory<ReconstruccionDbContext>
{
    public ReconstruccionDbContext CreateDbContext()
        => new(opciones);

    public Task<ReconstruccionDbContext> CreateDbContextAsync(
        CancellationToken cancelacion = default)
        => Task.FromResult(CreateDbContext());
}