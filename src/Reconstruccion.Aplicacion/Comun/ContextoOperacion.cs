using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Aplicacion.Comun;

/// <summary>Quien llama no tiene permiso para la operación (RN-RE-19).</summary>
public sealed class NoAutorizadoException(string mensaje) : Exception(mensaje);

/// <summary>El plan o proyecto que pide el comando no existe.</summary>
public sealed class NoEncontradoException(string mensaje) : Exception(mensaje);

/// <summary>
/// Estado de una operación en curso: quién la ejecuta, cuándo, qué agregados modificó y
/// qué se audita. Lo crea <see cref="EjecutorOperaciones"/> para cada caso de uso.
/// </summary>
public sealed class ContextoOperacion
{
    private readonly List<AgregadoRaiz> _agregados = [];
    private readonly List<(string TipoObjeto, Guid ObjetoId, Guid DesastreId, string? Detalle)> _auditoria = [];

    internal ContextoOperacion(IUsuarioActual usuario, DateTimeOffset ahora)
    {
        Usuario = usuario;
        Ahora = ahora;
    }

    public IUsuarioActual Usuario { get; }

    public DateTimeOffset Ahora { get; }

    /// <summary>Objeto principal de la operación; se usa también para auditar un rechazo.</summary>
    public (string Tipo, Guid Id, Guid DesastreId) Objetivo { get; private set; } = ("Desconocido", Guid.Empty, Guid.Empty);

    internal IReadOnlyList<AgregadoRaiz> Agregados => _agregados;

    internal IReadOnlyList<(string TipoObjeto, Guid ObjetoId, Guid DesastreId, string? Detalle)> EntradasAuditoria => _auditoria;

    public void FijarObjetivo(string tipo, Guid id, Guid desastreId) => Objetivo = (tipo, id, desastreId);

    /// <summary>
    /// Registra un agregado cuyos eventos deben pasar al outbox al confirmar.
    /// Devuelve el mismo agregado para encadenar.
    /// </summary>
    public T Seguir<T>(T agregado)
        where T : AgregadoRaiz
    {
        if (!_agregados.Contains(agregado))
        {
            _agregados.Add(agregado);
        }

        return agregado;
    }

    /// <summary>Concurrencia optimista: la versión esperada debe ser la vigente (RN-RE-18).</summary>
    public void VerificarVersion(AgregadoRaiz agregado, long esperada, string nombre)
    {
        if (!agregado.TieneVersion(esperada))
        {
            throw new ConflictoDeVersionException(
                $"La versión de {nombre} es {agregado.Version} y la solicitud esperaba {esperada}.");
        }
    }

    /// <summary>Agrega una entrada de auditoría que se confirma con la operación (RN-RE-16).</summary>
    public void Auditar(string tipoObjeto, Guid objetoId, Guid desastreId, string? detalle = null) =>
        _auditoria.Add((tipoObjeto, objetoId, desastreId, detalle));
}
