using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Dominio.Proyectos;

/// <summary>Aporte de financiación de una fuente (RN-RE-06).</summary>
public sealed record Aporte(string Fuente, decimal Monto);

/// <summary>Responsable de un organismo participante (RN-RE-06).</summary>
public sealed record Responsable(Guid UsuarioId, Guid OrganismoId);

public sealed record Contratista(string Identificacion, string Nombre);

/// <summary>Referencia a una evidencia; el binario no se guarda en este proyecto (RN-RE-10).</summary>
public sealed record ReferenciaEvidencia(string Identificador, string Tipo, string Ubicacion);

/// <summary>Hito del cronograma con su peso y avance (RN-RE-07, RN-RE-10).</summary>
public sealed class Hito
{
    private Hito()
    {
        Nombre = string.Empty;
    }

    internal Hito(Guid id, string nombre, DateOnly fechaInicio, DateOnly fechaFin, decimal peso)
    {
        Id = id;
        Nombre = nombre;
        FechaInicio = fechaInicio;
        FechaFin = fechaFin;
        Peso = peso;
    }

    public Guid Id { get; private set; }

    public string Nombre { get; private set; }

    public DateOnly FechaInicio { get; private set; }

    public DateOnly FechaFin { get; private set; }

    /// <summary>Peso del hito; los pesos suman 100 y quedan fijos después de Iniciar.</summary>
    public decimal Peso { get; private set; }

    public decimal Avance { get; private set; }

    public bool EstaCompleto => Avance == 100;

    internal void Reprogramar(DateOnly inicio, DateOnly fin)
    {
        FechaInicio = inicio;
        FechaFin = fin;
    }

    internal void ActualizarAvance(decimal avance) => Avance = avance;
}

/// <summary>Avance registrado de un hito, con fechas efectiva y de registro (RN-RE-17).</summary>
public sealed class Avance
{
    private readonly List<ReferenciaEvidencia> _evidencias = [];

    private Avance()
    {
    }

    internal Avance(Guid id, Guid hitoId, decimal porcentaje, decimal porcentajeAnterior, DateOnly fechaEfectiva,
        DateTimeOffset fechaRegistro, IEnumerable<ReferenciaEvidencia> evidencias, string? motivoCorreccion)
    {
        Id = id;
        HitoId = hitoId;
        Porcentaje = porcentaje;
        PorcentajeAnterior = porcentajeAnterior;
        FechaEfectiva = fechaEfectiva;
        FechaRegistro = fechaRegistro;
        MotivoCorreccion = motivoCorreccion;
        _evidencias.AddRange(evidencias);
    }

    public Guid Id { get; private set; }

    public Guid HitoId { get; private set; }

    public decimal Porcentaje { get; private set; }

    /// <summary>Valor anterior; se conserva en las correcciones (RN-RE-10).</summary>
    public decimal PorcentajeAnterior { get; private set; }

    public DateOnly FechaEfectiva { get; private set; }

    public DateTimeOffset FechaRegistro { get; private set; }

    public string? MotivoCorreccion { get; private set; }

    public IReadOnlyList<ReferenciaEvidencia> Evidencias => _evidencias;
}

/// <summary>
/// Costo registrado. Nunca se borra: se corrige con un reverso con motivo (RN-RE-11).
/// </summary>
public sealed class Costo
{
    private Costo()
    {
        Concepto = string.Empty;
    }

    internal Costo(Guid id, decimal monto, string concepto, DateOnly fechaEfectiva, DateTimeOffset fechaRegistro,
        Guid? costoRevertidoId, string? motivoReverso)
    {
        Id = id;
        Monto = monto;
        Concepto = concepto;
        FechaEfectiva = fechaEfectiva;
        FechaRegistro = fechaRegistro;
        CostoRevertidoId = costoRevertidoId;
        MotivoReverso = motivoReverso;
    }

    public Guid Id { get; private set; }

    /// <summary>Monto positivo del registro; en un reverso es el monto del costo revertido.</summary>
    public decimal Monto { get; private set; }

    public string Concepto { get; private set; }

    public DateOnly FechaEfectiva { get; private set; }

    public DateTimeOffset FechaRegistro { get; private set; }

    public Guid? CostoRevertidoId { get; private set; }

    public string? MotivoReverso { get; private set; }

    public bool EsReverso => CostoRevertidoId is not null;

    /// <summary>Aporte al costo ejecutado: negativo en un reverso.</summary>
    public decimal MontoConSigno => EsReverso ? -Monto : Monto;
}

/// <summary>Referencia a un documento del proyecto; el acta de entrega es requisito de Finalizar (RN-RE-12).</summary>
public sealed class Documento
{
    private Documento()
    {
        Nombre = string.Empty;
        Ubicacion = string.Empty;
    }

    internal Documento(Guid id, TipoDocumento tipo, string nombre, string ubicacion, DateTimeOffset fechaRegistro)
    {
        Id = id;
        Tipo = tipo;
        Nombre = nombre;
        Ubicacion = ubicacion;
        FechaRegistro = fechaRegistro;
    }

    public Guid Id { get; private set; }

    public TipoDocumento Tipo { get; private set; }

    public string Nombre { get; private set; }

    public string Ubicacion { get; private set; }

    public DateTimeOffset FechaRegistro { get; private set; }
}

/// <summary>
/// Cambio presupuestal (RN-RE-08, RN-RE-09). Nunca se sobrescribe: conserva el valor
/// anterior, el nuevo, quién solicitó, quién resolvió y cuándo.
/// </summary>
public sealed class CambioPresupuestal
{
    private readonly List<Aporte> _nuevosAportes = [];

    private CambioPresupuestal()
    {
        Motivo = string.Empty;
    }

    internal CambioPresupuestal(Guid id, decimal presupuestoAnterior, decimal presupuestoNuevo,
        IEnumerable<Aporte> nuevosAportes, int versionPresupuestoBase, Guid solicitanteId, string motivo, DateTimeOffset fecha)
    {
        Id = id;
        PresupuestoAnterior = presupuestoAnterior;
        PresupuestoNuevo = presupuestoNuevo;
        VersionPresupuestoBase = versionPresupuestoBase;
        SolicitanteId = solicitanteId;
        Motivo = motivo;
        FechaSolicitud = fecha;
        Estado = EstadoCambioPresupuestal.Solicitado;
        _nuevosAportes.AddRange(nuevosAportes);
    }

    public Guid Id { get; private set; }

    public EstadoCambioPresupuestal Estado { get; private set; }

    public decimal PresupuestoAnterior { get; private set; }

    public decimal PresupuestoNuevo { get; private set; }

    public IReadOnlyList<Aporte> NuevosAportes => _nuevosAportes;

    /// <summary>Versión del presupuesto sobre la que se pidió; debe seguir vigente al resolver (RN-RE-08).</summary>
    public int VersionPresupuestoBase { get; private set; }

    public Guid SolicitanteId { get; private set; }

    public Guid? ResolutorId { get; private set; }

    public string Motivo { get; private set; }

    public string? MotivoResolucion { get; private set; }

    public DateTimeOffset FechaSolicitud { get; private set; }

    public DateTimeOffset? FechaResolucion { get; private set; }

    internal void Resolver(EstadoCambioPresupuestal estado, Guid? resolutorId, string? motivo, DateTimeOffset fecha)
    {
        Estado = estado;
        ResolutorId = resolutorId;
        MotivoResolucion = motivo;
        FechaResolucion = fecha;
    }
}
