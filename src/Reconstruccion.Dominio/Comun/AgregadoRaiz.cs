namespace Reconstruccion.Dominio.Comun;

/// <summary>
/// Raíz de agregado con versión de negocio (RN-RE-18).
/// La versión se incrementa en toda operación que modifica el agregado,
/// aunque solo agregue registros hijos, y es la misma que usa la
/// infraestructura como token de concurrencia.
/// </summary>
public abstract class AgregadoRaiz
{
    private readonly List<IEventoDominio> _eventos = [];

    protected AgregadoRaiz(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("El identificador del agregado no puede ser vacío.", nameof(id));
        }

        Id = id;
    }

    /// <summary>Constructor reservado para la reconstrucción desde persistencia.</summary>
    protected AgregadoRaiz()
    {
    }

    public Guid Id { get; private set; }

    public long Version { get; private set; }

    public IReadOnlyList<IEventoDominio> Eventos => _eventos;

    /// <summary>Indica si la versión del agregado coincide con la que espera el comando.</summary>
    public bool TieneVersion(long versionEsperada) => Version == versionEsperada;

    /// <summary>
    /// Incrementa la versión. Toda operación que modifica el agregado debe llamarlo una vez.
    /// </summary>
    protected void IncrementarVersion() => Version++;

    protected void Registrar(IEventoDominio evento) => _eventos.Add(evento);

    /// <summary>Lo usa la unidad de trabajo después de pasar los eventos al outbox.</summary>
    public void LimpiarEventos() => _eventos.Clear();
}
