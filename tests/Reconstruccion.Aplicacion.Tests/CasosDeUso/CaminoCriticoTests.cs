using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Aplicacion.Proyectos;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Aplicacion.Tests.Memoria;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Aplicacion.Tests.CasosDeUso;

/// <summary>
/// Camino crítico de la demostración (secciones 4 y 9 del diseño), con los puertos en memoria.
/// </summary>
public class CaminoCriticoTests
{
    private readonly Entorno _e = new();
    private readonly Guid _desastre = Guid.NewGuid();
    private readonly Guid _responsable = Guid.NewGuid();
    private readonly Guid _autoridad = Guid.NewGuid();
    private readonly Guid _otraAutoridad = Guid.NewGuid();
    private readonly Guid _caracterizacion = Guid.NewGuid();
    private readonly Guid _sistema = Guid.NewGuid();

    [Fact]
    public async Task Flujo_completo_desde_las_evaluaciones_hasta_el_plan_Cerrado()
    {
        // 1. Caracterización envía dos evaluaciones validadas (una escuela y un puente).
        var recepcion = await RecibirAsync(2);
        Assert.Equal(2, recepcion.NecesidadesCreadas.Count);

        // 2. Un Responsable crea el proyecto con ambas necesidades y queda como formulador.
        var creado = await CrearProyectoAsync(recepcion.NecesidadesCreadas, recepcion.VersionPlan);
        var proyecto = _e.Proyectos.Single();
        Assert.Equal(_responsable, proyecto.FormuladorId);

        // 3. El formulador prepara presupuesto y cronograma; la Autoridad asigna participantes.
        var v = await Ok(new EditarPresupuestoPropuestoManejador(_e.Ejecutor, _e).ManejarAsync(new EditarPresupuestoPropuesto(
            Guid.NewGuid(), creado.ProyectoId, creado.NuevaVersion, 1500m, [new AporteDto("Nacion", 1000m), new AporteDto("Departamento", 500m)])));
        v = await Ok(new DefinirCronogramaManejador(_e.Ejecutor, _e).ManejarAsync(new DefinirCronograma(
            Guid.NewGuid(), creado.ProyectoId, v.NuevaVersion, _e.Hoy.AddDays(-20), _e.Hoy.AddDays(40),
            [new HitoDto("Obra gris", _e.Hoy.AddDays(-20), _e.Hoy.AddDays(10), 70m), new HitoDto("Acabados", _e.Hoy.AddDays(11), _e.Hoy.AddDays(40), 30m)])));
        _e.ComoUsuario(_autoridad, Rol.Autoridad);
        v = await Ok(new AsignarParticipantesManejador(_e.Ejecutor, _e).ManejarAsync(new AsignarParticipantes(
            Guid.NewGuid(), creado.ProyectoId, v.NuevaVersion, new ResponsableDto(_responsable, Guid.NewGuid()), new ContratistaDto("900123", "Obras SAS"))));

        // 4. La Autoridad aprueba y el Responsable inicia.
        v = await Ok(new AprobarProyectoManejador(_e.Ejecutor, _e).ManejarAsync(new AprobarProyecto(Guid.NewGuid(), creado.ProyectoId, v.NuevaVersion)));
        _e.ComoUsuario(_responsable, Rol.Responsable);
        v = await Ok(new IniciarProyectoManejador(_e.Ejecutor, _e).ManejarAsync(new IniciarProyecto(Guid.NewGuid(), creado.ProyectoId, v.NuevaVersion)));
        Assert.Equal(EstadoProyecto.EnEjecucion, proyecto.Estado);

        // 5. Avances, costo y acta.
        foreach (var hito in proyecto.Hitos)
        {
            v = await Ok(new RegistrarAvanceManejador(_e.Ejecutor, _e).ManejarAsync(new RegistrarAvance(
                Guid.NewGuid(), creado.ProyectoId, v.NuevaVersion, hito.Id, 100m, _e.Hoy,
                [new ReferenciaEvidenciaDto("foto-" + hito.Nombre, "Fotografía", "evidencias/" + hito.Id)])));
        }

        v = await Ok(new RegistrarCostoManejador(_e.Ejecutor, _e).ManejarAsync(new RegistrarCosto(
            Guid.NewGuid(), creado.ProyectoId, v.NuevaVersion, 1450m, "Obra", _e.Hoy)));
        v = await Ok(new AdjuntarDocumentoManejador(_e.Ejecutor, _e).ManejarAsync(new AdjuntarDocumento(
            Guid.NewGuid(), creado.ProyectoId, v.NuevaVersion, TipoDocumento.ActaEntrega, "Acta de entrega", "docs/acta.pdf")));

        // 6. Caracterización envía la señal de fin; el plan todavía no se completa.
        var fin = await FinAsync(cantidad: 2);
        Assert.Equal(RespuestaFinCaracterizacion.Aceptada, fin.Respuesta);
        Assert.Equal(EstadoPlan.Abierto, fin.EstadoPlan);

        // 7. Finalizar el proyecto atiende las necesidades y completa el plan.
        var finalizado = await Ok(new FinalizarProyectoManejador(_e.Ejecutor, _e, _e).ManejarAsync(new FinalizarProyecto(
            Guid.NewGuid(), creado.ProyectoId, v.NuevaVersion, fin.VersionPlan!.Value)));
        var plan = _e.Planes.Single();
        Assert.Equal(EstadoProyecto.Finalizado, proyecto.Estado);
        Assert.Equal(EstadoPlan.Completado, plan.Estado);
        var completado = Assert.Single(_e.Outbox.OfType<PlanCompletado>());
        Assert.Equal(finalizado.NuevaVersionPlan, completado.VersionAgregado);

        // 8. Cierre acepta la versión notificada y el plan queda Cerrado.
        _e.ComoUsuario(_sistema, Rol.Sistema);
        var cierre = await Ok(new RegistrarAceptacionCierreManejador(_e.Ejecutor, _e).ManejarAsync(
            new RegistrarAceptacionCierre(_desastre, completado.VersionAgregado)));
        Assert.True(cierre.PlanCerrado);
        Assert.Equal(EstadoPlan.Cerrado, plan.Estado);

        // Cada operación quedó auditada y no hubo rechazos.
        Assert.Empty(_e.Rechazos);
        Assert.Contains(_e.Auditoria, a => a.Accion == nameof(AprobarProyecto) && a.UsuarioId == _autoridad);
        Assert.Contains(_e.Auditoria, a => a.Accion == nameof(FinalizarProyecto) && a.TipoObjeto == "Plan");
    }

    // --- Idempotencia y concurrencia (RN-RE-18) -------------------------------

    [Fact]
    public async Task Una_solicitud_repetida_devuelve_el_resultado_original_sin_ejecutarse_de_nuevo()
    {
        var recepcion = await RecibirAsync(1);
        var comando = new CrearProyecto(Guid.NewGuid(), _desastre, recepcion.VersionPlan, "Puente El Tablazo", "",
            TipoProyecto.Reconstruccion, recepcion.NecesidadesCreadas);
        var manejador = new CrearProyectoManejador(_e.Ejecutor, _e, _e);
        _e.ComoUsuario(_responsable, Rol.Responsable);

        var primero = await Ok(manejador.ManejarAsync(comando));
        var confirmaciones = _e.Confirmaciones;
        var repetido = await Ok(manejador.ManejarAsync(comando));

        Assert.Equal(primero, repetido);
        Assert.Single(_e.Proyectos);
        Assert.Equal(confirmaciones, _e.Confirmaciones);
    }

    [Fact]
    public async Task El_mismo_RequestId_con_datos_distintos_se_rechaza()
    {
        var recepcion = await RecibirAsync(2);
        var requestId = Guid.NewGuid();
        var manejador = new CrearProyectoManejador(_e.Ejecutor, _e, _e);
        _e.ComoUsuario(_responsable, Rol.Responsable);
        await Ok(manejador.ManejarAsync(new CrearProyecto(requestId, _desastre, recepcion.VersionPlan, "Escuela", "",
            TipoProyecto.Reconstruccion, [recepcion.NecesidadesCreadas[0]])));

        var resultado = await manejador.ManejarAsync(new CrearProyecto(requestId, _desastre, recepcion.VersionPlan + 1, "Otra", "",
            TipoProyecto.Reconstruccion, [recepcion.NecesidadesCreadas[1]]));

        Assert.Equal(CodigoError.SolicitudRepetidaConDatosDistintos, resultado.Error!.Codigo);
    }

    [Fact]
    public async Task Una_version_desactualizada_produce_conflicto_y_se_audita_el_rechazo()
    {
        var creado = await ProyectoCreadoAsync();

        var resultado = await new EditarPresupuestoPropuestoManejador(_e.Ejecutor, _e).ManejarAsync(new EditarPresupuestoPropuesto(
            Guid.NewGuid(), creado.ProyectoId, creado.NuevaVersion + 5, 100m, [new AporteDto("Nacion", 100m)]));

        Assert.Equal(CodigoError.ConflictoVersion, resultado.Error!.Codigo);
        Assert.Equal("RN-RE-18", resultado.Error.Regla);
        var rechazo = Assert.Single(_e.Rechazos);
        Assert.Equal(creado.ProyectoId, rechazo.ObjetoId);
        Assert.Equal(ResultadoOperacion.Rechazado, rechazo.Resultado);
    }

    [Fact]
    public async Task Un_conflicto_al_confirmar_se_traduce_a_ConflictoVersion()
    {
        var creado = await ProyectoCreadoAsync();
        _e.SimularConflictoAlConfirmar = true;

        var resultado = await new EditarPresupuestoPropuestoManejador(_e.Ejecutor, _e).ManejarAsync(new EditarPresupuestoPropuesto(
            Guid.NewGuid(), creado.ProyectoId, creado.NuevaVersion, 100m, [new AporteDto("Nacion", 100m)]));

        Assert.Equal(CodigoError.ConflictoVersion, resultado.Error!.Codigo);
        Assert.DoesNotContain(_e.Solicitudes, s => s.TipoComando == nameof(EditarPresupuestoPropuesto));
    }

    // --- Autorización (RN-RE-19) -----------------------------------------------

    [Fact]
    public async Task En_Propuesto_solo_el_formulador_edita_el_presupuesto()
    {
        var creado = await ProyectoCreadoAsync();
        _e.ComoUsuario(Guid.NewGuid(), Rol.Responsable);

        var resultado = await new EditarPresupuestoPropuestoManejador(_e.Ejecutor, _e).ManejarAsync(new EditarPresupuestoPropuesto(
            Guid.NewGuid(), creado.ProyectoId, creado.NuevaVersion, 100m, [new AporteDto("Nacion", 100m)]));

        Assert.Equal(CodigoError.NoAutorizado, resultado.Error!.Codigo);
        Assert.Equal("RN-RE-19", resultado.Error.Regla);
    }

    [Fact]
    public async Task Solo_una_Autoridad_aprueba()
    {
        var creado = await ProyectoCreadoAsync();

        var resultado = await new AprobarProyectoManejador(_e.Ejecutor, _e).ManejarAsync(
            new AprobarProyecto(Guid.NewGuid(), creado.ProyectoId, creado.NuevaVersion));

        Assert.Equal(CodigoError.NoAutorizado, resultado.Error!.Codigo);
    }

    [Fact]
    public async Task Solo_Caracterizacion_usa_su_contrato()
    {
        _e.ComoUsuario(_responsable, Rol.Responsable);

        var resultado = await new RecibirNecesidadesManejador(_e.Ejecutor, _e, _e).ManejarAsync(
            new RecibirNecesidades(_desastre, 1, [Evaluacion()]));

        Assert.Equal(CodigoError.NoAutorizado, resultado.Error!.Codigo);
        Assert.Empty(_e.Planes);
    }

    // --- Reglas de dominio y señal de fin --------------------------------------

    [Fact]
    public async Task Una_regla_incumplida_se_informa_con_su_codigo_y_no_confirma()
    {
        var creado = await ProyectoCreadoAsync();
        _e.ComoUsuario(_autoridad, Rol.Autoridad);
        var confirmaciones = _e.Confirmaciones;

        // Sin presupuesto ni responsable no se puede aprobar (RN-RE-06).
        var resultado = await new AprobarProyectoManejador(_e.Ejecutor, _e).ManejarAsync(
            new AprobarProyecto(Guid.NewGuid(), creado.ProyectoId, creado.NuevaVersion));

        Assert.Equal(CodigoError.ReglaIncumplida, resultado.Error!.Codigo);
        Assert.Equal("RN-RE-06", resultado.Error.Regla);
        Assert.Equal(confirmaciones, _e.Confirmaciones);
        Assert.Single(_e.Rechazos);
    }

    [Fact]
    public async Task Sin_plan_la_senal_de_fin_con_cantidad_cero_no_crea_plan()
    {
        var resultado = await FinAsync(cantidad: 0);

        Assert.Equal(RespuestaFinCaracterizacion.NoHayPlanAplicable, resultado.Respuesta);
        Assert.Empty(_e.Planes);
    }

    [Fact]
    public async Task Sin_plan_la_senal_de_fin_con_evaluaciones_esperadas_responde_que_faltan()
    {
        var resultado = await FinAsync(cantidad: 3);

        Assert.Equal(RespuestaFinCaracterizacion.FaltanEvaluaciones, resultado.Respuesta);
    }

    [Fact]
    public async Task Un_proyecto_inexistente_responde_NoEncontrado()
    {
        _e.ComoUsuario(_responsable, Rol.Responsable);

        var resultado = await new IniciarProyectoManejador(_e.Ejecutor, _e).ManejarAsync(
            new IniciarProyecto(Guid.NewGuid(), Guid.NewGuid(), 0));

        Assert.Equal(CodigoError.NoEncontrado, resultado.Error!.Codigo);
    }

    // --- Ayudas -----------------------------------------------------------------

    private static EvaluacionRecibidaDto Evaluacion() =>
        new(Guid.NewGuid(), 1, Guid.NewGuid(), "Escuela", "Grave", DateTimeOffset.UtcNow.AddDays(-2),
            new UbicacionDto(4.6, -74.1, null), Validada: true, RequiereIntervencion: true);

    private async Task<ResultadoRecepcionNecesidades> RecibirAsync(int cantidad)
    {
        _e.ComoUsuario(_caracterizacion, Rol.Caracterizacion);
        return await Ok(new RecibirNecesidadesManejador(_e.Ejecutor, _e, _e).ManejarAsync(
            new RecibirNecesidades(_desastre, 1, Enumerable.Range(0, cantidad).Select(_ => Evaluacion()).ToList())));
    }

    private async Task<ResultadoFinCaracterizacion> FinAsync(int cantidad)
    {
        _e.ComoUsuario(_caracterizacion, Rol.Caracterizacion);
        var resultado = await Ok(new RecibirFinCaracterizacionManejador(_e.Ejecutor, _e).ManejarAsync(
            new RecibirFinCaracterizacion(_desastre, 1, cantidad)));
        _e.ComoUsuario(_responsable, Rol.Responsable);
        return resultado;
    }

    private async Task<ResultadoComandoConPlan> CrearProyectoAsync(IReadOnlyList<Guid> necesidades, long versionPlan)
    {
        _e.ComoUsuario(_responsable, Rol.Responsable);
        return await Ok(new CrearProyectoManejador(_e.Ejecutor, _e, _e).ManejarAsync(new CrearProyecto(
            Guid.NewGuid(), _desastre, versionPlan, "Escuela y puente de La Esperanza", "Reconstrucción",
            TipoProyecto.Reconstruccion, necesidades)));
    }

    private async Task<ResultadoComandoConPlan> ProyectoCreadoAsync()
    {
        var recepcion = await RecibirAsync(1);
        return await CrearProyectoAsync(recepcion.NecesidadesCreadas, recepcion.VersionPlan);
    }

    private static async Task<T> Ok<T>(Task<Resultado<T>> tarea)
    {
        var resultado = await tarea;
        Assert.True(resultado.EsExitoso, resultado.Error?.ToString());
        return resultado.Valor;
    }
}
