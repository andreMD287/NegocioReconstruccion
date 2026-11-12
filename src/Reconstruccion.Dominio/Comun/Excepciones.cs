namespace Reconstruccion.Dominio.Comun;

/// <summary>
/// Una regla de negocio o el estado del agregado no permiten la operación.
/// Aplicación la traduce a <c>CodigoError.ReglaIncumplida</c>.
/// </summary>
public sealed class ReglaDeNegocioException(string regla, string mensaje) : Exception(mensaje)
{
    /// <summary>Regla del diseño que se incumplió, por ejemplo "RN-RE-12".</summary>
    public string Regla { get; } = regla;
}

/// <summary>
/// Datos mal formados (montos con más de dos decimales, textos vacíos, fechas invertidas).
/// Aplicación la traduce a <c>CodigoError.DatosInvalidos</c>.
/// </summary>
public sealed class DatosInvalidosException(string mensaje) : Exception(mensaje);

/// <summary>
/// El elemento referenciado no existe dentro del agregado (necesidad, hito, costo, cambio, alerta).
/// Aplicación la traduce a <c>CodigoError.NoEncontrado</c>.
/// </summary>
public sealed class ElementoNoEncontradoException(string mensaje) : Exception(mensaje);
