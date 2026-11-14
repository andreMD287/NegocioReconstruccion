using System.Security.Claims;
using Reconstruccion.Aplicacion.Puertos;
using RolUsuario = Reconstruccion.Aplicacion.Puertos.Rol;

namespace Reconstruccion.Host.Seguridad;

/// <summary>Nombres de los claims que lleva el token JWT.</summary>
public static class Claims
{
    public const string Usuario = "sub";
    public const string Rol = "rol";
    public const string Organismo = "organismo";

    /// <summary>Roles que puede traer un token. Sistema nunca: solo lo usan los procesos internos.</summary>
    public static readonly RolUsuario[] RolesDeToken =
        [RolUsuario.Responsable, RolUsuario.Autoridad, RolUsuario.Auditor, RolUsuario.Caracterizacion];

    /// <summary>Valida los claims del token; devuelve null si son válidos o el motivo del rechazo.</summary>
    public static string? Validar(ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue(Usuario), out var id) || id == Guid.Empty)
        {
            return "El token no trae un identificador de usuario válido.";
        }

        if (!Enum.TryParse<RolUsuario>(principal.FindFirstValue(Rol), ignoreCase: false, out var rol) || !RolesDeToken.Contains(rol))
        {
            return "El token no trae un rol válido.";
        }

        var organismo = principal.FindFirstValue(Organismo);
        return organismo is not null && !Guid.TryParse(organismo, out _) ? "El organismo del token no es válido." : null;
    }
}

/// <summary>
/// <see cref="IUsuarioActual"/> de un alcance (petición gRPC, conexión SignalR o proceso interno).
/// En una petición sale de los claims del JWT, ya validados al autenticar; el rol nunca se toma
/// de un dato de la solicitud (RN-RE-19). Los procesos internos llaman <see cref="ComoSistema"/>.
/// </summary>
public sealed class UsuarioActualAmbito(IHttpContextAccessor accesoHttp) : IUsuarioActual
{
    /// <summary>Identificador fijo con que quedan auditadas las operaciones de los procesos internos.</summary>
    public static readonly Guid IdSistema = new("00000000-0000-0000-0000-00000000005a");

    private bool _esSistema;

    public Guid UsuarioId => _esSistema ? IdSistema : Guid.Parse(Claim(Claims.Usuario));

    public Rol Rol => _esSistema ? Rol.Sistema : Enum.Parse<Rol>(Claim(Claims.Rol));

    public Guid? OrganismoId
    {
        get
        {
            if (_esSistema)
            {
                return null;
            }

            var valor = accesoHttp.HttpContext?.User.FindFirstValue(Claims.Organismo);
            return valor is null ? null : Guid.Parse(valor);
        }
    }

    /// <summary>Lo llaman los procesos internos (vigilancia del cronograma, publicador del outbox).</summary>
    public void ComoSistema() => _esSistema = true;

    private string Claim(string tipo)
    {
        var usuario = accesoHttp.HttpContext?.User;
        if (usuario?.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("No hay un usuario autenticado en este alcance.");
        }

        return usuario.FindFirstValue(tipo) ?? throw new InvalidOperationException($"El token no trae el claim {tipo}.");
    }
}

/// <summary>Reloj del sistema en UTC; Npgsql exige offset 0 para timestamptz.</summary>
public sealed class RelojUtc : IReloj
{
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow;
}
