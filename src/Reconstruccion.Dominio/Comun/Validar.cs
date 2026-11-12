namespace Reconstruccion.Dominio.Comun;

/// <summary>Validaciones de forma compartidas por los agregados.</summary>
internal static class Validar
{
    /// <summary>Montos en una sola moneda con dos cifras decimales (RN-RE-06).</summary>
    public static decimal Monto(decimal monto, string campo, bool permitirCero = false)
    {
        if (monto < 0 || (!permitirCero && monto == 0))
        {
            throw new DatosInvalidosException($"{campo} debe ser mayor que cero.");
        }

        if (decimal.Round(monto, 2) != monto)
        {
            throw new DatosInvalidosException($"{campo} admite como máximo dos cifras decimales.");
        }

        return monto;
    }

    public static decimal Porcentaje(decimal valor, string campo)
    {
        if (valor < 0 || valor > 100)
        {
            throw new DatosInvalidosException($"{campo} debe estar entre 0 y 100.");
        }

        return valor;
    }

    public static string Texto(string? valor, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new DatosInvalidosException($"{campo} es obligatorio.");
        }

        return valor.Trim();
    }

    public static Guid Id(Guid valor, string campo)
    {
        if (valor == Guid.Empty)
        {
            throw new DatosInvalidosException($"{campo} es obligatorio.");
        }

        return valor;
    }

    /// <summary>Lanza una <see cref="ReglaDeNegocioException"/> si la condición no se cumple.</summary>
    public static void Regla(bool condicion, string regla, string mensaje)
    {
        if (!condicion)
        {
            throw new ReglaDeNegocioException(regla, mensaje);
        }
    }
}
