using Reconstruccion.Contratos.V1;
using static Reconstruccion.ClienteDemo.Formato;
using static Reconstruccion.ClienteDemo.Llamadas;

namespace Reconstruccion.ClienteDemo;

/// <summary>
/// Las dos pruebas de fallo que no quedan cubiertas por el final del escenario principal
/// (la tercera, Cierre no disponible, la reporta <see cref="EscenarioPrincipal"/>).
/// </summary>
internal static class EscenariosFallo
{
    public static async Task EjecutarAsync(CanalReconstruccion canal, HttpClient http, CancellationToken ct)
    {
        Consola.Titulo("Pruebas de fallo");
        await FinalizarSinCompletarHitosAsync(canal, http, ct);
        await EvaluacionRepetidaAsync(canal, http, ct);
    }

    /// <summary>Finalizar un proyecto con hitos incompletos y sin acta debe fallar por RN-RE-12.</summary>
    private static async Task FinalizarSinCompletarHitosAsync(CanalReconstruccion canal, HttpClient http, CancellationToken ct)
    {
        Consola.Paso("Prueba 1: finalizar un proyecto sin completar los hitos (RN-RE-12).");

        var caracterizacion = await Autenticacion.ObtenerAsync(http, "Caracterizacion");
        var formulador = await Autenticacion.ObtenerAsync(http, "Responsable");
        var autoridad = await Autenticacion.ObtenerAsync(http, "Autoridad");
        var desastreId = Guid.NewGuid();

        var recepcion = await canal.Caracterizacion.RecibirNecesidadesAsync(
            new RecibirNecesidadesRequest { DesastreId = desastreId.ToString(), VersionCiclo = 1, Evaluaciones = { NuevaEvaluacion() } },
            caracterizacion.Encabezados(), cancellationToken: ct).ResponseAsync;

        var creado = await canal.Proyectos.CrearProyectoAsync(
            new CrearProyectoRequest
            {
                RequestId = NuevoRequestId().ToString(), DesastreId = desastreId.ToString(), ExpectedVersionPlan = recepcion.VersionPlan,
                Nombre = "Proyecto incompleto", Descripcion = "Para demostrar RN-RE-12",
                Tipo = TipoProyectoGrpc.TipoProyectoRecuperacion, NecesidadIds = { recepcion.NecesidadesCreadas }
            }, formulador.Encabezados(), cancellationToken: ct).ResponseAsync;

        var version = (await canal.Proyectos.EditarPresupuestoPropuestoAsync(
            new EditarPresupuestoPropuestoRequest
            {
                RequestId = NuevoRequestId().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = creado.NuevaVersion,
                PresupuestoDecimal = Dec(10_000_000m), Aportes = { new AporteDtoGrpc { Fuente = "Nación", MontoDecimal = Dec(10_000_000m) } }
            }, formulador.Encabezados(), cancellationToken: ct).ResponseAsync).NuevaVersion;

        version = (await canal.Proyectos.AsignarParticipantesAsync(
            new AsignarParticipantesRequest
            {
                RequestId = NuevoRequestId().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version,
                Responsable = new ResponsableDtoGrpc { UsuarioId = formulador.Id.ToString(), OrganismoId = Guid.NewGuid().ToString() },
                Contratista = new ContratistaDtoGrpc { Identificacion = "900999888-1", Nombre = "Contratista de prueba" }
            }, autoridad.Encabezados(), cancellationToken: ct).ResponseAsync).NuevaVersion;

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        version = (await canal.Proyectos.DefinirCronogramaAsync(
            new DefinirCronogramaRequest
            {
                RequestId = NuevoRequestId().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version,
                FechaInicio = Fecha(hoy), FechaFin = Fecha(hoy.AddDays(10)),
                Hitos = { new HitoDtoGrpc { Nombre = "Única etapa", FechaInicio = Fecha(hoy), FechaFin = Fecha(hoy.AddDays(10)), PesoDecimal = Dec(100m) } }
            }, formulador.Encabezados(), cancellationToken: ct).ResponseAsync).NuevaVersion;

        version = (await canal.Proyectos.AprobarProyectoAsync(
            new AprobarProyectoRequest { RequestId = NuevoRequestId().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version },
            autoridad.Encabezados(), cancellationToken: ct).ResponseAsync).NuevaVersion;

        version = (await canal.Proyectos.IniciarProyectoAsync(
            new IniciarProyectoRequest { RequestId = NuevoRequestId().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version },
            formulador.Encabezados(), cancellationToken: ct).ResponseAsync).NuevaVersion;

        Consola.Paso("   (a propósito no se registra avance ni acta de entrega)");

        // Se espera FAILED_PRECONDITION con la regla RN-RE-12 en los trailers.
        await Intentar("FinalizarProyecto sin hitos completos ni acta (debe fallar)", () => canal.Proyectos.FinalizarProyectoAsync(
            new FinalizarProyectoRequest { RequestId = NuevoRequestId().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version, ExpectedVersionPlan = creado.NuevaVersionPlan },
            formulador.Encabezados(), cancellationToken: ct).ResponseAsync);
    }

    /// <summary>Un reenvío de la misma evaluación no duplica la necesidad (RN-RE-18).</summary>
    private static async Task EvaluacionRepetidaAsync(CanalReconstruccion canal, HttpClient http, CancellationToken ct)
    {
        Consola.Paso("Prueba 2: enviar dos veces la misma evaluación (RN-RE-18).");

        var caracterizacion = await Autenticacion.ObtenerAsync(http, "Caracterizacion");
        var desastreId = Guid.NewGuid();
        var evaluacion = NuevaEvaluacion();

        var primera = await canal.Caracterizacion.RecibirNecesidadesAsync(
            new RecibirNecesidadesRequest { DesastreId = desastreId.ToString(), VersionCiclo = 1, Evaluaciones = { evaluacion } },
            caracterizacion.Encabezados(), cancellationToken: ct).ResponseAsync;
        Consola.Paso($"   primer envío: {primera.NecesidadesCreadas.Count} necesidad(es) creada(s).");

        var segunda = await canal.Caracterizacion.RecibirNecesidadesAsync(
            new RecibirNecesidadesRequest { DesastreId = desastreId.ToString(), VersionCiclo = 1, Evaluaciones = { evaluacion } },
            caracterizacion.Encabezados(), cancellationToken: ct).ResponseAsync;

        var repetida = segunda.NoAceptadas.FirstOrDefault(n => n.EvaluacionId == evaluacion.EvaluacionId);
        if (segunda.NecesidadesCreadas.Count == 0 && repetida is { Motivo: MotivoEvaluacionNoAceptadaGrpc.MotivoEvaluacionRepetida })
        {
            Consola.Ok("El reenvío no duplicó la necesidad: quedó marcado como Repetida.");
        }
        else
        {
            Consola.Error($"El reenvío no se comportó como se esperaba: {segunda.NecesidadesCreadas.Count} necesidades creadas de nuevo.");
        }
    }

    private static EvaluacionRecibida NuevaEvaluacion() => new()
    {
        EvaluacionId = Guid.NewGuid().ToString(), VersionEvaluacion = 1, ActivoId = Guid.NewGuid().ToString(),
        TipoActivo = "Vivienda", NivelDano = "Grave", FechaValidacion = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
        Ubicacion = new UbicacionEvaluacion { Latitud = 4.6, Longitud = -74.1 }, Validada = true, RequiereIntervencion = true
    };
}
