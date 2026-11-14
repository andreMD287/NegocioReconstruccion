using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Reconstruccion.Aplicacion.Comun;

namespace Reconstruccion.Adaptadores.Grpc.Servicios;

/// <summary>
/// Traducción entre los mensajes .proto y los tipos de Aplicación (sección 2: las entidades
/// de dominio no cruzan la frontera gRPC). Un dato mal formado responde INVALID_ARGUMENT.
/// </summary>
public static class TraduccionGrpc
{
    /// <summary>Trailer con la regla incumplida, por ejemplo "RN-RE-12".</summary>
    public const string TrailerRegla = "regla";

    // --- Entrada ----------------------------------------------------------------

    public static Guid Id(string valor, string campo) =>
        Guid.TryParse(valor, out var id) && id != Guid.Empty
            ? id
            : throw Invalido($"{campo} debe ser un identificador válido.");

    public static Guid? IdOpcional(string? valor, string campo) =>
        string.IsNullOrWhiteSpace(valor) ? null : Id(valor, campo);

    public static List<Guid> Ids(IEnumerable<string> valores, string campo) => valores.Select(v => Id(v, campo)).ToList();

    /// <summary>Montos y porcentajes viajan como texto decimal exacto, nunca double (sección 8.1).</summary>
    public static decimal Decimal(string valor, string campo) =>
        decimal.TryParse(valor, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw Invalido($"{campo} debe ser un decimal con punto, por ejemplo \"1500.00\".");

    public static DateOnly Fecha(string valor, string campo) =>
        DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha
            : throw Invalido($"{campo} debe tener el formato YYYY-MM-DD.");

    public static DateTimeOffset Instante(Timestamp? valor, string campo) =>
        valor is null ? throw Invalido($"{campo} es obligatorio.") : valor.ToDateTimeOffset();

    /// <summary>
    /// Los enums gRPC repiten el orden de los de Aplicación y Dominio con un valor 0
    /// "no especificado" adelante; el 0 o un valor desconocido son datos inválidos.
    /// </summary>
    public static TDestino Enum<TDestino>(System.Enum valor, string campo)
        where TDestino : struct, System.Enum
    {
        var numero = Convert.ToInt32(valor, CultureInfo.InvariantCulture) - 1;
        return numero >= 0 && System.Enum.IsDefined(typeof(TDestino), numero)
            ? (TDestino)System.Enum.ToObject(typeof(TDestino), numero)
            : throw Invalido($"{campo} no está especificado o no es válido.");
    }

    // --- Salida -------------------------------------------------------------------

    public static TDestino EnumGrpc<TDestino>(System.Enum valor)
        where TDestino : struct, System.Enum =>
        (TDestino)System.Enum.ToObject(typeof(TDestino), Convert.ToInt32(valor, CultureInfo.InvariantCulture) + 1);

    public static string Texto(decimal valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Texto(DateOnly fecha) => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Timestamp Marca(DateTimeOffset instante) => Timestamp.FromDateTimeOffset(instante);

    /// <summary>
    /// Devuelve el valor de un resultado exitoso o lanza el <see cref="RpcException"/>
    /// correspondiente al código de error (sección 8.1), con la regla en los trailers.
    /// </summary>
    public static T Valor<T>(Resultado<T> resultado)
    {
        if (resultado.EsExitoso)
        {
            return resultado.Valor;
        }

        var error = resultado.Error!;
        var trailers = new Metadata();
        if (error.Regla is not null)
        {
            trailers.Add(TrailerRegla, error.Regla);
        }

        throw new RpcException(new Status(Codigo(error.Codigo), error.Mensaje), trailers);
    }

    public static StatusCode Codigo(CodigoError codigo) => codigo switch
    {
        CodigoError.DatosInvalidos => StatusCode.InvalidArgument,
        CodigoError.ReglaIncumplida => StatusCode.FailedPrecondition,
        CodigoError.ConflictoVersion => StatusCode.Aborted,
        CodigoError.NoAutorizado => StatusCode.PermissionDenied,
        CodigoError.SolicitudRepetidaConDatosDistintos => StatusCode.AlreadyExists,
        CodigoError.NoEncontrado => StatusCode.NotFound,
        _ => StatusCode.Internal
    };

    private static RpcException Invalido(string mensaje) => new(new Status(StatusCode.InvalidArgument, mensaje));
}
