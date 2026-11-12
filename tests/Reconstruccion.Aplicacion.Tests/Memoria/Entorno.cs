using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;

namespace Reconstruccion.Aplicacion.Tests.Memoria;

/// <summary>
/// Implementaciones en memoria de los puertos de Aplicación. Lo que se registra en la
/// transacción queda pendiente hasta que la unidad de trabajo confirma.
/// </summary>
internal sealed class Entorno : IRepositorioPlanes, IRepositorioProyectos, IUnidadDeTrabajo, IRegistroAuditoria,
    IRegistroSolicitudes, IOutbox, IReloj
{
    private readonly List<EntradaAuditoria> _auditoriaPendiente = [];
    private readonly List<SolicitudProcesada> _solicitudesPendientes = [];
    private readonly List<IEventoDominio> _eventosPendientes = [];

    public Entorno()
    {
        Usuario = new UsuarioDePrueba();
        Ejecutor = new EjecutorOperaciones(this, this, this, this, Usuario, this);
    }

    public DateTimeOffset Ahora { get; set; } = new(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(-5));

    public DateOnly Hoy => DateOnly.FromDateTime(Ahora.Date);

    public UsuarioDePrueba Usuario { get; }

    public EjecutorOperaciones Ejecutor { get; }

    public List<PlanReconstruccion> Planes { get; } = [];

    public List<ProyectoReconstruccion> Proyectos { get; } = [];

    public List<EntradaAuditoria> Auditoria { get; } = [];

    public List<EntradaAuditoria> Rechazos { get; } = [];

    public List<SolicitudProcesada> Solicitudes { get; } = [];

    public List<IEventoDominio> Outbox { get; } = [];

    public int Confirmaciones { get; private set; }

    /// <summary>Si se fija, la próxima confirmación simula que otro cambio ganó la carrera.</summary>
    public bool SimularConflictoAlConfirmar { get; set; }

    public void ComoUsuario(Guid usuarioId, Rol rol) => Usuario.Cambiar(usuarioId, rol);

    // --- IRepositorioPlanes / IRepositorioProyectos -------------------------

    public Task<PlanReconstruccion?> ObtenerPorDesastreAsync(Guid desastreId, CancellationToken cancelacion = default) =>
        Task.FromResult(Planes.FirstOrDefault(p => p.DesastreId == desastreId));

    public void Agregar(PlanReconstruccion plan) => Planes.Add(plan);

    public Task<ProyectoReconstruccion?> ObtenerPorIdAsync(Guid proyectoId, CancellationToken cancelacion = default) =>
        Task.FromResult(Proyectos.FirstOrDefault(p => p.Id == proyectoId));

    public void Agregar(ProyectoReconstruccion proyecto) => Proyectos.Add(proyecto);

    public Task<IReadOnlyList<Guid>> ListarIdsEnEjecucionAsync(CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(Proyectos.Where(p => p.Estado == EstadoProyecto.EnEjecucion).Select(p => p.Id).ToList());

    // --- IUnidadDeTrabajo ----------------------------------------------------

    public Task ConfirmarAsync(CancellationToken cancelacion = default)
    {
        if (SimularConflictoAlConfirmar)
        {
            SimularConflictoAlConfirmar = false;
            _auditoriaPendiente.Clear();
            _solicitudesPendientes.Clear();
            _eventosPendientes.Clear();
            throw new ConflictoDeVersionException("Otro cambio confirmó antes.");
        }

        Confirmaciones++;
        Auditoria.AddRange(_auditoriaPendiente);
        Solicitudes.AddRange(_solicitudesPendientes);
        Outbox.AddRange(_eventosPendientes);
        _auditoriaPendiente.Clear();
        _solicitudesPendientes.Clear();
        _eventosPendientes.Clear();
        return Task.CompletedTask;
    }

    // --- IRegistroAuditoria --------------------------------------------------

    public void Registrar(EntradaAuditoria entrada) => _auditoriaPendiente.Add(entrada);

    public Task RegistrarRechazoAsync(EntradaAuditoria entrada, CancellationToken cancelacion = default)
    {
        Rechazos.Add(entrada);
        return Task.CompletedTask;
    }

    // --- IRegistroSolicitudes ------------------------------------------------

    public Task<SolicitudProcesada?> ObtenerAsync(Guid requestId, CancellationToken cancelacion = default) =>
        Task.FromResult(Solicitudes.FirstOrDefault(s => s.RequestId == requestId));

    public void Registrar(SolicitudProcesada solicitud) => _solicitudesPendientes.Add(solicitud);

    // --- IOutbox -------------------------------------------------------------

    public void Agregar(IEnumerable<IEventoDominio> eventos) => _eventosPendientes.AddRange(eventos);
}

internal sealed class UsuarioDePrueba : IUsuarioActual
{
    public Guid UsuarioId { get; private set; } = Guid.NewGuid();

    public Rol Rol { get; private set; } = Rol.Responsable;

    public Guid? OrganismoId { get; private set; } = Guid.NewGuid();

    public void Cambiar(Guid usuarioId, Rol rol)
    {
        UsuarioId = usuarioId;
        Rol = rol;
    }
}
