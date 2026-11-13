using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Infraestructura.Persistencia.Registros;

public sealed class AuditoriaPersistida
{
    private AuditoriaPersistida() { }

    public AuditoriaPersistida(EntradaAuditoria entrada)
    {
        Id = Guid.NewGuid();
        UsuarioId = entrada.UsuarioId;
        Rol = entrada.Rol;
        Accion = entrada.Accion;
        TipoObjeto = entrada.TipoObjeto;
        ObjetoId = entrada.ObjetoId;
        DesastreId = entrada.DesastreId;
        Fecha = entrada.Fecha;
        Resultado = entrada.Resultado;
        RequestId = entrada.RequestId;
        Detalle = entrada.Detalle;
    }

    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Rol Rol { get; private set; }
    public string Accion { get; private set; } = string.Empty;
    public string TipoObjeto { get; private set; } = string.Empty;
    public Guid ObjetoId { get; private set; }
    public Guid DesastreId { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    public ResultadoOperacion Resultado { get; private set; }
    public Guid? RequestId { get; private set; }
    public string? Detalle { get; private set; }
}

public sealed class SolicitudProcesadaPersistida
{
    private SolicitudProcesadaPersistida() { }

    public SolicitudProcesadaPersistida(SolicitudProcesada solicitud)
    {
        RequestId = solicitud.RequestId;
        TipoComando = solicitud.TipoComando;
        HuellaContenido = solicitud.HuellaContenido;
        RespuestaSerializada = solicitud.RespuestaSerializada;
    }

    public Guid RequestId { get; private set; }
    public string TipoComando { get; private set; } = string.Empty;
    public string HuellaContenido { get; private set; } = string.Empty;
    public string RespuestaSerializada { get; private set; } = string.Empty;

    public SolicitudProcesada ComoContrato() =>
        new(RequestId, TipoComando, HuellaContenido, RespuestaSerializada);
}

public sealed class EventoOutboxPersistido
{
    private EventoOutboxPersistido() { }

    public EventoOutboxPersistido(
        Guid eventoId,
        Guid desastreId,
        Guid agregadoId,
        long versionAgregado,
        DateTimeOffset ocurridoEn,
        string tipoEvento,
        string contenidoJson)
    {
        Id = eventoId;
        DesastreId = desastreId;
        AgregadoId = agregadoId;
        VersionAgregado = versionAgregado;
        OcurridoEn = ocurridoEn;
        TipoEvento = tipoEvento;
        ContenidoJson = contenidoJson;
    }

    public Guid Id { get; private set; }

    // PostgreSQL asigna el orden cuando se inserta la fila.
    public long Secuencia { get; private set; }

    public Guid DesastreId { get; private set; }
    public Guid AgregadoId { get; private set; }
    public long VersionAgregado { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }

    public string TipoEvento { get; private set; } = string.Empty;
    public string ContenidoJson { get; private set; } = "{}";

    public DateTimeOffset? PublicadoEn { get; private set; }
    public int Intentos { get; private set; }
    public string? UltimoError { get; private set; }

    public void MarcarPublicado(DateTimeOffset fecha)
    {
        PublicadoEn = fecha;
        Intentos++;
        UltimoError = null;
    }

    public void RegistrarIntentoFallido(string motivo)
    {
        Intentos++;
        UltimoError = motivo;
    }
}