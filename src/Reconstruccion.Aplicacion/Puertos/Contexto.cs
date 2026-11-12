namespace Reconstruccion.Aplicacion.Puertos;

/// <summary>Roles que reconoce la autorización contextual (RN-RE-19).</summary>
public enum Rol
{
    Responsable,
    Autoridad,
    Auditor,

    /// <summary>Cliente del contrato gRPC de Caracterización.</summary>
    Caracterizacion,

    /// <summary>Procesos internos: publicador del outbox y VigilanciaCronograma.</summary>
    Sistema
}

/// <summary>
/// Identidad de quien llama. El adaptador la obtiene del mecanismo de autenticación;
/// el rol nunca se toma de un dato libre de la solicitud.
/// </summary>
public interface IUsuarioActual
{
    Guid UsuarioId { get; }

    Rol Rol { get; }

    /// <summary>Organismo del usuario; RN-RE-06 exige un responsable de un organismo participante.</summary>
    Guid? OrganismoId { get; }
}

/// <summary>Fuente de la fecha y hora actual, para poder fijarla en pruebas.</summary>
public interface IReloj
{
    DateTimeOffset Ahora { get; }
}
