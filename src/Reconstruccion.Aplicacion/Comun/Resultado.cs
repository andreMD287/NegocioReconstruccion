namespace Reconstruccion.Aplicacion.Comun;

/// <summary>
/// Categorías de error de negocio. El adaptador gRPC las traduce a códigos de estado
/// (sección 8.1 del diseño).
/// </summary>
public enum CodigoError
{
    /// <summary>Datos mal formados. gRPC: INVALID_ARGUMENT.</summary>
    DatosInvalidos,

    /// <summary>Una regla o el estado no permiten la operación. gRPC: FAILED_PRECONDITION.</summary>
    ReglaIncumplida,

    /// <summary>La versión esperada no coincide con la vigente (RN-RE-18). gRPC: ABORTED.</summary>
    ConflictoVersion,

    /// <summary>Quien llama no tiene permiso para la operación (RN-RE-19). gRPC: PERMISSION_DENIED.</summary>
    NoAutorizado,

    /// <summary>Mismo RequestId con datos distintos (RN-RE-18). gRPC: ALREADY_EXISTS.</summary>
    SolicitudRepetidaConDatosDistintos,

    /// <summary>El plan, proyecto, necesidad o elemento no existe. gRPC: NOT_FOUND.</summary>
    NoEncontrado
}

/// <summary>Error de negocio con la regla que lo produjo, si aplica.</summary>
public sealed record Error(CodigoError Codigo, string Mensaje, string? Regla = null);

/// <summary>Resultado de una operación sin valor de retorno.</summary>
public class Resultado
{
    protected Resultado(Error? error) => Error = error;

    public Error? Error { get; }

    public bool EsExitoso => Error is null;

    public static Resultado Exito() => new(null);

    public static Resultado Fallo(Error error) => new(error);

    public static Resultado Fallo(CodigoError codigo, string mensaje, string? regla = null) =>
        new(new Error(codigo, mensaje, regla));
}

/// <summary>Resultado de una operación que devuelve un valor cuando es exitosa.</summary>
public sealed class Resultado<T> : Resultado
{
    private readonly T? _valor;

    private Resultado(T valor)
        : base(null) => _valor = valor;

    private Resultado(Error error)
        : base(error)
    {
    }

    public T Valor => EsExitoso
        ? _valor!
        : throw new InvalidOperationException("Un resultado fallido no tiene valor.");

    public static Resultado<T> Exito(T valor) => new(valor);

    public static new Resultado<T> Fallo(Error error) => new(error);

    public static new Resultado<T> Fallo(CodigoError codigo, string mensaje, string? regla = null) =>
        new(new Error(codigo, mensaje, regla));

    public static implicit operator Resultado<T>(Error error) => Fallo(error);
}
