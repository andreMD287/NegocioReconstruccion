using System.Runtime.CompilerServices;
using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Aplicacion.Tests.Memoria;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;

namespace Reconstruccion.Aplicacion.Tests.Integracion;

public class DistribucionEventosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Desastre = Guid.NewGuid();
    private static readonly Guid Agregado = Guid.NewGuid();

    // --- Catálogo de notificaciones (tabla 8.2) -----------------------------------

    [Fact]
    public void Los_eventos_de_proyecto_van_a_los_grupos_de_la_tabla_8_2()
    {
        Assert.Equal([$"proyecto-{Agregado}"], Grupos(new AvanceRegistrado(Desastre, Agregado, 3, Ahora, Guid.NewGuid(), 50, 30)));
        Assert.Equal([$"desastre-{Desastre}", $"proyecto-{Agregado}"],
            Grupos(new ProyectoEstadoCambiado(Desastre, Agregado, 3, Ahora, EstadoProyecto.Aprobado, EstadoProyecto.EnEjecucion, null)));
        Assert.Equal([$"desastre-{Desastre}", $"proyecto-{Agregado}"], Grupos(new CostoRegistrado(Desastre, Agregado, 3, Ahora, Guid.NewGuid(), false)));
        Assert.Equal([$"desastre-{Desastre}", $"proyecto-{Agregado}"], Grupos(new CambioPresupuestalSolicitado(Desastre, Agregado, 3, Ahora, Guid.NewGuid())));
        Assert.Equal([$"desastre-{Desastre}"], Grupos(new ProyectoCreado(Desastre, Agregado, 0, Ahora, [Guid.NewGuid()])));
    }

    [Fact]
    public void Los_eventos_del_plan_se_identifican_por_el_desastre()
    {
        var notificacion = CatalogoNotificaciones.Para(new PlanCompletado(Desastre, Agregado, 7, Ahora))!;

        Assert.Equal(Desastre, notificacion.ObjetoId);
        Assert.Equal(7, notificacion.Version);
        Assert.Equal(nameof(PlanCompletado), notificacion.Tipo);
        Assert.Equal([$"desastre-{Desastre}"], notificacion.Grupos);
    }

    [Fact]
    public void Una_alerta_del_plan_solo_va_al_grupo_del_desastre()
    {
        var delPlan = new AlertaGenerada(Desastre, Agregado, 2, Ahora, Guid.NewGuid(), TipoAlerta.OrigenModificado, Guid.NewGuid(), EnPlan: true);
        var delProyecto = new AlertaGenerada(Desastre, Agregado, 2, Ahora, Guid.NewGuid(), TipoAlerta.Sobrecosto, Agregado);

        Assert.Equal([$"desastre-{Desastre}"], Grupos(delPlan));
        Assert.Equal([$"desastre-{Desastre}", $"proyecto-{Agregado}"], Grupos(delProyecto));
    }

    [Fact]
    public void Todo_evento_del_dominio_tiene_notificacion_salvo_DocumentoAdjuntado()
    {
        var tipos = typeof(IEventoDominio).Assembly.GetTypes()
            .Where(t => typeof(IEventoDominio).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

        var sinNotificacion = tipos
            .Where(t => CatalogoNotificaciones.Para((IEventoDominio)RuntimeHelpers.GetUninitializedObject(t)) is null)
            .Select(t => t.Name)
            .ToList();

        Assert.Equal([nameof(DocumentoAdjuntado)], sinNotificacion);
    }

    // --- Distribuidor ----------------------------------------------------------------

    [Fact]
    public async Task Un_evento_sin_notificacion_se_da_por_entregado()
    {
        var (distribuidor, notificador, cierre, _) = Crear();

        var r = await distribuidor.DistribuirAsync(new DocumentoAdjuntado(Desastre, Agregado, 2, Ahora, Guid.NewGuid(), TipoDocumento.Contrato));

        Assert.True(r.Entregado);
        Assert.Empty(notificador.Enviadas);
        Assert.Equal(0, cierre.Llamadas);
    }

    [Fact]
    public async Task Si_Cierre_acepta_el_plan_completado_pasa_a_Cerrado()
    {
        var (distribuidor, notificador, cierre, entorno) = Crear();
        var plan = PlanCompletadoEn(entorno);

        var r = await distribuidor.DistribuirAsync(new PlanCompletado(plan.DesastreId, plan.Id, plan.Version, Ahora));

        Assert.True(r.Entregado);
        Assert.Single(notificador.Enviadas);
        Assert.Equal(1, cierre.Llamadas);
        Assert.Equal(EstadoPlan.Cerrado, plan.Estado);
    }

    [Fact]
    public async Task Si_Cierre_no_esta_disponible_el_evento_se_reintenta()
    {
        var (distribuidor, _, cierre, entorno) = Crear();
        var plan = PlanCompletadoEn(entorno);
        cierre.Falla = true;

        var r = await distribuidor.DistribuirAsync(new PlanCompletado(plan.DesastreId, plan.Id, plan.Version, Ahora));

        Assert.False(r.Entregado);
        Assert.Equal(EstadoPlan.Completado, plan.Estado);
    }

    [Fact]
    public async Task Si_Cierre_rechaza_la_notificacion_el_evento_se_reintenta()
    {
        var (distribuidor, _, cierre, entorno) = Crear();
        var plan = PlanCompletadoEn(entorno);
        cierre.Acepta = false;

        var r = await distribuidor.DistribuirAsync(new PlanCompletado(plan.DesastreId, plan.Id, plan.Version, Ahora));

        Assert.False(r.Entregado);
        Assert.Equal(EstadoPlan.Completado, plan.Estado);
    }

    [Fact]
    public async Task Una_aceptacion_de_version_superada_no_cierra_el_plan()
    {
        var (distribuidor, _, _, entorno) = Crear();
        var plan = PlanCompletadoEn(entorno);
        var versionNotificada = plan.Version;
        plan.ReactivarNecesidad(plan.Necesidades[0].Id, "Se confirmó el daño", Ahora);

        var r = await distribuidor.DistribuirAsync(new PlanCompletado(plan.DesastreId, plan.Id, versionNotificada, Ahora));

        Assert.True(r.Entregado);
        Assert.Equal(EstadoPlan.Abierto, plan.Estado);
    }

    [Fact]
    public async Task La_reapertura_se_notifica_a_Cierre()
    {
        var (distribuidor, _, cierre, _) = Crear();

        var r = await distribuidor.DistribuirAsync(new PlanReabierto(Desastre, Agregado, 9, Ahora));

        Assert.True(r.Entregado);
        Assert.Equal(1, cierre.Llamadas);
    }

    [Fact]
    public async Task Si_falla_SignalR_no_se_llama_a_Cierre_y_se_reintenta()
    {
        var (distribuidor, notificador, cierre, entorno) = Crear();
        var plan = PlanCompletadoEn(entorno);
        notificador.Falla = true;

        var r = await distribuidor.DistribuirAsync(new PlanCompletado(plan.DesastreId, plan.Id, plan.Version, Ahora));

        Assert.False(r.Entregado);
        Assert.Equal(0, cierre.Llamadas);
    }

    // --- Catálogo de manejadores ---------------------------------------------------------

    [Fact]
    public void El_catalogo_registra_un_manejador_por_comando_y_consulta()
    {
        var servicios = CatalogoAplicacion.Manejadores.Select(m => m.Servicio).ToList();

        Assert.Equal(29, servicios.Count); // 26 comandos y 3 consultas
        Assert.Equal(servicios.Count, servicios.Distinct().Count());
        Assert.All(CatalogoAplicacion.Manejadores, m => Assert.True(m.Servicio.IsAssignableFrom(m.Implementacion)));
    }

    // --- Ayudas ---------------------------------------------------------------------------

    private static string[] Grupos(IEventoDominio evento) => CatalogoNotificaciones.Para(evento)!.Grupos.ToArray();

    private static (DistribuidorEventos, NotificadorFalso, CierreFalso, Entorno) Crear()
    {
        var entorno = new Entorno();
        entorno.ComoUsuario(Guid.NewGuid(), Rol.Sistema);
        var notificador = new NotificadorFalso();
        var cierre = new CierreFalso();
        var distribuidor = new DistribuidorEventos(notificador, cierre, new RegistrarAceptacionCierreManejador(entorno.Ejecutor, entorno));
        return (distribuidor, notificador, cierre, entorno);
    }

    private static PlanReconstruccion PlanCompletadoEn(Entorno entorno)
    {
        var plan = PlanReconstruccion.Abrir(Guid.NewGuid(), Desastre);
        plan.RecibirEvaluaciones(1, [new EvaluacionEntrante(new EvaluacionOrigen(Guid.NewGuid(), 1, Guid.NewGuid(), "Escuela", "Grave",
            Ahora, new Ubicacion(4.6, -74.1, null)), true, true)], Guid.NewGuid, Ahora);
        plan.DescartarNecesidad(plan.Necesidades[0].Id, "No requiere obra", Ahora);
        plan.RecibirSenalFin(1, 1, Ahora);
        Assert.Equal(EstadoPlan.Completado, plan.Estado);
        plan.LimpiarEventos();
        entorno.Planes.Add(plan);
        return plan;
    }

    private sealed class NotificadorFalso : INotificadorTiempoReal
    {
        public List<Notificacion> Enviadas { get; } = [];

        public bool Falla { get; set; }

        public Task NotificarAsync(Notificacion notificacion, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("Hub no disponible");
            }

            Enviadas.Add(notificacion);
            return Task.CompletedTask;
        }
    }

    private sealed class CierreFalso : IServicioCierre
    {
        public int Llamadas { get; private set; }

        public bool Falla { get; set; }

        public bool Acepta { get; set; } = true;

        public Task<RespuestaCierre> NotificarPlanCompletadoAsync(Guid desastreId, long versionPlan, CancellationToken cancelacion = default) =>
            Responder();

        public Task<RespuestaCierre> NotificarReaperturaAsync(Guid desastreId, long versionPlan, CancellationToken cancelacion = default) =>
            Responder();

        private Task<RespuestaCierre> Responder()
        {
            Llamadas++;
            return Falla
                ? throw new TimeoutException("Cierre no responde")
                : Task.FromResult(new RespuestaCierre(Acepta, Acepta ? null : "Versión en revisión"));
        }
    }
}
