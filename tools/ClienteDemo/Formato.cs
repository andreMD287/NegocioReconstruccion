using System.Globalization;
using Grpc.Core;

namespace Reconstruccion.ClienteDemo;

/// <summary>
/// Formatos que exige el contrato gRPC (sección 8.1): montos y porcentajes como decimal
/// exacto con punto, nunca double; fechas como YYYY-MM-DD.
/// </summary>
internal static class Formato
{
    public static string Dec(decimal valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Fecha(DateOnly fecha) => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Guid NuevoRequestId() => Guid.NewGuid();
}

/// <summary>Ejecuta una llamada gRPC, imprime el resultado y nunca deja que una <see cref="RpcException"/> detenga la demo.</summary>
internal static class Llamadas
{
    public static async Task<T?> Intentar<T>(string etiqueta, Func<Task<T>> accion)
        where T : class
    {
        try
        {
            var resultado = await accion();
            Consola.Ok(etiqueta);
            return resultado;
        }
        catch (RpcException ex)
        {
            var regla = ex.Trailers.FirstOrDefault(t => t.Key == "regla")?.Value;
            Consola.Error($"{etiqueta}: {ex.StatusCode} - {ex.Status.Detail}" + (regla is null ? "" : $" (regla {regla})"));
            return null;
        }
    }
}
