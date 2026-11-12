using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Aplicacion.Puertos;

public enum ResultadoOperacion
{
    Exitoso,
    Rechazado
}

/// <summary>
/// Registro de auditoría: quién, qué, cuándo, sobre qué y resultado (RN-RE-16).
/// </summary>
public sealed record EntradaAuditoria(
    Guid UsuarioId,
    Rol Rol,
    string Accion,
    string TipoObjeto,
    Guid ObjetoId,
    Guid DesastreId,
    DateTimeOffset Fecha,
    ResultadoOperacion Resultado,
    Guid? RequestId,
    string? Detalle);

/// <summary>Auditoría de solo adición (RN-RE-16).</summary>
public interface IRegistroAuditoria
{
    /// <summary>Agrega la entrada a la transacción en curso; se confirma con la unidad de trabajo.</summary>
    void Registrar(EntradaAuditoria entrada);

    /// <summary>
    /// Escribe un rechazo en una transacción independiente, para que no se pierda
    /// al revertir la operación (RN-RE-16).
    /// </summary>
    Task RegistrarRechazoAsync(EntradaAuditoria entrada, CancellationToken cancelacion = default);
}

/// <summary>Solicitud ya procesada, para la idempotencia (RN-RE-18).</summary>
/// <param name="HuellaContenido">Huella de los datos del comando, para detectar un RequestId reutilizado con datos distintos.</param>
/// <param name="RespuestaSerializada">Respuesta original serializada por Aplicación.</param>
public sealed record SolicitudProcesada(Guid RequestId, string TipoComando, string HuellaContenido, string RespuestaSerializada);

/// <summary>Registro de solicitudes procesadas (RN-RE-18).</summary>
public interface IRegistroSolicitudes
{
    Task<SolicitudProcesada?> ObtenerAsync(Guid requestId, CancellationToken cancelacion = default);

    /// <summary>Agrega la solicitud a la transacción en curso; se confirma con la unidad de trabajo.</summary>
    void Registrar(SolicitudProcesada solicitud);
}

/// <summary>
/// Outbox: los eventos se guardan en la misma transacción que el cambio y un publicador
/// los entrega después de confirmarla (sección 8.2).
/// </summary>
public interface IOutbox
{
    /// <summary>Agrega los eventos a la transacción en curso; se confirman con la unidad de trabajo.</summary>
    void Agregar(IEnumerable<IEventoDominio> eventos);
}
