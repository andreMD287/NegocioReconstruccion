using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Proyectos;

namespace Reconstruccion.Aplicacion.Comun;

/// <summary>Autorización contextual (RN-RE-19).</summary>
public static class Autorizacion
{
    /// <summary>Exige que quien llama tenga alguno de los roles indicados.</summary>
    public static void ExigirRol(IUsuarioActual usuario, params Rol[] roles)
    {
        if (!roles.Contains(usuario.Rol))
        {
            throw new NoAutorizadoException($"La operación requiere el rol {string.Join(" o ", roles)}.");
        }
    }

    /// <summary>
    /// Exige que quien llama pueda operar el proyecto: en Propuesto, su formulador;
    /// desde Aprobado, el Responsable asignado según la asignación vigente (RN-RE-19).
    /// </summary>
    public static void ExigirOperador(IUsuarioActual usuario, ProyectoReconstruccion proyecto)
    {
        if (proyecto.Estado == EstadoProyecto.Propuesto)
        {
            if (usuario.UsuarioId != proyecto.FormuladorId)
            {
                throw new NoAutorizadoException("En Propuesto, el proyecto lo opera su formulador.");
            }

            return;
        }

        if (usuario.Rol != Rol.Responsable || proyecto.ResponsableId != usuario.UsuarioId)
        {
            throw new NoAutorizadoException("El proyecto lo opera el Responsable asignado.");
        }
    }
}
