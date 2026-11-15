using Microsoft.AspNetCore.SignalR.Client;
using Reconstruccion.Contratos.V1;
using static Reconstruccion.ClienteDemo.Formato;
using static Reconstruccion.ClienteDemo.Llamadas;

namespace Reconstruccion.ClienteDemo;

/// <summary>
/// Escenario E del enunciado: la Autoridad identifica activos que requieren intervención,
/// se crea un proyecto, se asigna presupuesto y responsable, se hace seguimiento del avance
/// y se finaliza. Recorre los diez pasos de la figura 9 del diseño, de principio a fin.
/// </summary>
internal static class EscenarioPrincipal
{
    public static async Task EjecutarAsync(Uri baseUri, CanalReconstruccion canal, HttpClient http, CancellationToken ct)
    {
        Consola.Titulo("Escenario E: de las evaluaciones de Caracterización al plan Cerrado");

        var caracterizacion = await Autenticacion.ObtenerAsync(http, "Caracterizacion");
        var formulador = await Autenticacion.ObtenerAsync(http, "Responsable");
        var autoridad = await Autenticacion.ObtenerAsync(http, "Autoridad");
        var auditor = await Autenticacion.ObtenerAsync(http, "Auditor");
        var desastreId = Guid.NewGuid();
        Consola.Paso($"Desastre {desastreId}; formulador/Responsable {formulador.Id}; Autoridad {autoridad.Id}.");

        await using var hub = await ConectarHubAsync(baseUri, autoridad, desastreId, ct);

        // Paso 1: Caracterización identifica 2 activos que requieren intervención (sección 4.6).
        var evaluacionEscuela = NuevaEvaluacion("Escuela", "Grave");
        var evaluacionPuente = NuevaEvaluacion("Puente", "DestrucciónTotal");
        var recepcion = await Intentar("1. RecibirNecesidades (escuela y puente)", () => canal.Caracterizacion.RecibirNecesidadesAsync(
            new RecibirNecesidadesRequest { DesastreId = desastreId.ToString(), VersionCiclo = 1, Evaluaciones = { evaluacionEscuela, evaluacionPuente } },
            caracterizacion.Encabezados(), cancellationToken: ct).ResponseAsync);
        if (recepcion is null)
        {
            return;
        }

        Consola.Paso($"   necesidades creadas: {string.Join(", ", recepcion.NecesidadesCreadas)}; versión del plan {recepcion.VersionPlan}.");

        // Paso 2 y 3: el Responsable crea el proyecto y propone presupuesto y aportes.
        var creado = await Intentar("2. CrearProyecto", () => canal.Proyectos.CrearProyectoAsync(
            new CrearProyectoRequest
            {
                RequestId = NuevoRequestId().ToString(), DesastreId = desastreId.ToString(), ExpectedVersionPlan = recepcion.VersionPlan,
                Nombre = "Escuela y puente de La Esperanza", Descripcion = "Reconstrucción tras el sismo",
                Tipo = TipoProyectoGrpc.TipoProyectoReconstruccion, NecesidadIds = { recepcion.NecesidadesCreadas }
            }, formulador.Encabezados(), cancellationToken: ct).ResponseAsync);
        if (creado is null)
        {
            return;
        }

        var proyectoId = creado.ProyectoId;
        var versionPlan = creado.NuevaVersionPlan; // AsignarNecesidades ya incrementó la versión del plan.
        await hub.InvokeAsync("UnirseAProyecto", Guid.Parse(proyectoId), ct);

        var version = (await Intentar("3. EditarPresupuestoPropuesto (150.000.000,00)", () => canal.Proyectos.EditarPresupuestoPropuestoAsync(
            new EditarPresupuestoPropuestoRequest
            {
                RequestId = NuevoRequestId().ToString(), ProyectoId = proyectoId, ExpectedVersion = creado.NuevaVersion,
                PresupuestoDecimal = Dec(150_000_000m),
                Aportes = { new AporteDtoGrpc { Fuente = "Nación", MontoDecimal = Dec(100_000_000m) }, new AporteDtoGrpc { Fuente = "Departamento", MontoDecimal = Dec(50_000_000m) } }
            }, formulador.Encabezados(), cancellationToken: ct).ResponseAsync))?.NuevaVersion ?? creado.NuevaVersion;

        // Paso 4: la Autoridad asigna responsable y contratista.
        version = (await Intentar("4. AsignarParticipantes", () => canal.Proyectos.AsignarParticipantesAsync(
            new AsignarParticipantesRequest
            {
                RequestId = NuevoRequestId().ToString(), ProyectoId = proyectoId, ExpectedVersion = version,
                Responsable = new ResponsableDtoGrpc { UsuarioId = formulador.Id.ToString(), OrganismoId = Guid.NewGuid().ToString() },
                Contratista = new ContratistaDtoGrpc { Identificacion = "900123456-7", Nombre = "Obras Civiles SAS" }
            }, autoridad.Encabezados(), cancellationToken: ct).ResponseAsync))?.NuevaVersion ?? version;

        // Paso 5: el Responsable define el cronograma (60 % cimentación, 40 % acabados).
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        version = (await Intentar("5. DefinirCronograma", () => canal.Proyectos.DefinirCronogramaAsync(
            new DefinirCronogramaRequest
            {
                RequestId = NuevoRequestId().ToString(), ProyectoId = proyectoId, ExpectedVersion = version,
                FechaInicio = Fecha(hoy), FechaFin = Fecha(hoy.AddDays(40)),
                Hitos =
                {
                    new HitoDtoGrpc { Nombre = "Cimentación", FechaInicio = Fecha(hoy), FechaFin = Fecha(hoy.AddDays(15)), PesoDecimal = Dec(60m) },
                    new HitoDtoGrpc { Nombre = "Acabados", FechaInicio = Fecha(hoy.AddDays(16)), FechaFin = Fecha(hoy.AddDays(40)), PesoDecimal = Dec(40m) }
                }
            }, formulador.Encabezados(), cancellationToken: ct).ResponseAsync))?.NuevaVersion ?? version;

        // Paso 6: la Autoridad aprueba (distinta del formulador, RN-RE-06).
        version = (await Intentar("6. AprobarProyecto", () => canal.Proyectos.AprobarProyectoAsync(
            new AprobarProyectoRequest { RequestId = NuevoRequestId().ToString(), ProyectoId = proyectoId, ExpectedVersion = version },
            autoridad.Encabezados(), cancellationToken: ct).ResponseAsync))?.NuevaVersion ?? version;

        // Paso 7: el Responsable inicia la ejecución.
        version = (await Intentar("7. IniciarProyecto", () => canal.Proyectos.IniciarProyectoAsync(
            new IniciarProyectoRequest { RequestId = NuevoRequestId().ToString(), ProyectoId = proyectoId, ExpectedVersion = version },
            formulador.Encabezados(), cancellationToken: ct).ResponseAsync))?.NuevaVersion ?? version;

        var detalle = await canal.Consultas.ConsultarDetalleProyectoAsync(
            new ConsultarDetalleProyectoRequest { ProyectoId = proyectoId }, auditor.Encabezados(), cancellationToken: ct);

        // Paso 8: avances al 100 % de cada hito, con evidencias.
        foreach (var hito in detalle.Hitos)
        {
            version = (await Intentar($"8. RegistrarAvance (hito {hito.Nombre})", () => canal.Proyectos.RegistrarAvanceAsync(
                new RegistrarAvanceRequest
                {
                    RequestId = NuevoRequestId().ToString(), ProyectoId = proyectoId, ExpectedVersion = version, HitoId = hito.HitoId,
                    PorcentajeDecimal = Dec(100m), FechaEfectiva = Fecha(hoy),
                    Evidencias = { new ReferenciaEvidenciaDtoGrpc { Identificador = $"foto-{hito.Nombre}", Tipo = "Fotografía", Ubicacion = $"evidencias/{hito.HitoId}.jpg" } }
                }, formulador.Encabezados(), cancellationToken: ct).ResponseAsync))?.NuevaVersion ?? version;
        }

        // Paso 9: el costo ejecutado, dentro del presupuesto.
        version = (await Intentar("9. RegistrarCosto (145.000.000,00)", () => canal.Proyectos.RegistrarCostoAsync(
            new RegistrarCostoRequest
            {
                RequestId = NuevoRequestId().ToString(), ProyectoId = proyectoId, ExpectedVersion = version,
                MontoDecimal = Dec(145_000_000m), Concepto = "Obra civil", FechaEfectiva = Fecha(hoy)
            }, formulador.Encabezados(), cancellationToken: ct).ResponseAsync))?.NuevaVersion ?? version;

        // Paso 10: el acta de entrega, requisito de RN-RE-12.
        version = (await Intentar("10. AdjuntarDocumento (acta de entrega)", () => canal.Proyectos.AdjuntarDocumentoAsync(
            new AdjuntarDocumentoRequest
            {
                RequestId = NuevoRequestId().ToString(), ProyectoId = proyectoId, ExpectedVersion = version,
                Tipo = TipoDocumentoGrpc.TipoDocumentoActaEntrega, Nombre = "Acta de entrega", Ubicacion = "docs/acta-entrega.pdf"
            }, formulador.Encabezados(), cancellationToken: ct).ResponseAsync))?.NuevaVersion ?? version;

        // Caracterización ya terminó este ciclo: señal de fin con la cantidad esperada (RN-RE-22).
        var fin = await Intentar("Señal de fin de caracterización (2 evaluaciones esperadas)", () => canal.Caracterizacion.RecibirFinCaracterizacionAsync(
            new RecibirFinCaracterizacionRequest { DesastreId = desastreId.ToString(), VersionCiclo = 1, CantidadEsperada = 2 },
            caracterizacion.Encabezados(), cancellationToken: ct).ResponseAsync);
        if (fin is not null)
        {
            Consola.Paso($"   respuesta: {fin.Respuesta}; plan {(fin.HasEstadoPlan ? fin.EstadoPlan.ToString() : "sin plan")}.");
            if (fin.HasVersionPlan)
            {
                versionPlan = fin.VersionPlan; // RecibirSenalFin también incrementa la versión del plan.
            }
        }

        // El proyecto se finaliza: las necesidades pasan a Atendida y el plan evalúa su completitud.
        var finalizado = await Intentar("Finalizar proyecto", () => canal.Proyectos.FinalizarProyectoAsync(
            new FinalizarProyectoRequest { RequestId = NuevoRequestId().ToString(), ProyectoId = proyectoId, ExpectedVersion = version, ExpectedVersionPlan = versionPlan },
            formulador.Encabezados(), cancellationToken: ct).ResponseAsync);

        if (finalizado is not null)
        {
            Consola.Paso($"   proyecto Finalizado; plan en versión {finalizado.NuevaVersionPlan}.");
            await EsperarCompletitudDelPlanAsync(canal, desastreId, autoridad, ct);
        }

        var historial = await canal.Consultas.ConsultarHistorialAuditoriaAsync(
            new ConsultarHistorialAuditoriaRequest { DesastreId = desastreId.ToString(), Pagina = 1, TamanoPagina = 50 },
            auditor.Encabezados(), cancellationToken: ct);
        Consola.Paso($"Historial de auditoría del desastre: {historial.Total} entradas registradas.");
    }

    /// <summary>
    /// Espera a que el publicador del outbox entregue los eventos (sección 5.3) y reporta si el
    /// plan llegó a Cerrado o si, por no tener un Cierre disponible, se quedó en Completado:
    /// es la tercera prueba de fallo de la demostración (sección 9.5).
    /// </summary>
    private static async Task EsperarCompletitudDelPlanAsync(CanalReconstruccion canal, Guid desastreId, Usuario autoridad, CancellationToken ct)
    {
        Consola.Paso("Esperando al publicador del outbox (SignalR y, si Cierre está disponible, el cierre del plan)...");
        EstadoPlanGrpc ultimoEstado = EstadoPlanGrpc.EstadoPlanNoEspecificado;
        for (var intento = 1; intento <= 4; intento++)
        {
            await Task.Delay(TimeSpan.FromSeconds(4), ct);
            var seguimiento = await canal.Consultas.ConsultarSeguimientoConsolidadoAsync(
                new ConsultarSeguimientoConsolidadoRequest { DesastreId = desastreId.ToString() }, autoridad.Encabezados(), cancellationToken: ct);
            ultimoEstado = seguimiento.EstadoPlan;
            Consola.Paso($"   [{intento}/4] estado del plan: {ultimoEstado}.");
            if (ultimoEstado == EstadoPlanGrpc.EstadoPlanCerrado)
            {
                break;
            }
        }

        if (ultimoEstado == EstadoPlanGrpc.EstadoPlanCerrado)
        {
            Consola.Ok("Cierre aceptó la notificación y el plan quedó Cerrado.");
        }
        else
        {
            Consola.Error("Prueba de fallo 3: el plan permanece Completado. Cierre no está disponible o no aceptó la " +
                "notificación; el publicador del outbox seguirá reintentando (sección 5.3).");
        }
    }

    private static EvaluacionRecibida NuevaEvaluacion(string tipoActivo, string nivelDano) => new()
    {
        EvaluacionId = Guid.NewGuid().ToString(), VersionEvaluacion = 1, ActivoId = Guid.NewGuid().ToString(),
        TipoActivo = tipoActivo, NivelDano = nivelDano, FechaValidacion = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
        Ubicacion = new UbicacionEvaluacion { Latitud = 4.6, Longitud = -74.1, Descripcion = "Zona urbana" },
        Validada = true, RequiereIntervencion = true
    };

    private static async Task<HubConnection> ConectarHubAsync(Uri baseUri, Usuario autoridad, Guid desastreId, CancellationToken ct)
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(baseUri, "hubs/reconstruccion"), opciones =>
            {
                opciones.AccessTokenProvider = () => Task.FromResult<string?>(autoridad.Token);
                opciones.HttpMessageHandlerFactory = _ => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
            })
            .Build();

        hub.On<NotificacionRecibida>("Notificacion", n => Consola.Evento($"{n.Tipo} id={n.ObjetoId} versión {n.Version}"));
        await hub.StartAsync(ct);
        await hub.InvokeAsync("UnirseADesastre", desastreId, ct);
        return hub;
    }

    /// <summary>Forma del objeto anónimo que envía <c>NotificadorSignalR</c> (sección 8.2).</summary>
    private sealed record NotificacionRecibida(string Tipo, Guid ObjetoId, long Version);
}
