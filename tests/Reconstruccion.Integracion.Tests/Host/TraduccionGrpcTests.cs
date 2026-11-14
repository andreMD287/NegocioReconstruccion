using Grpc.Core;
using Reconstruccion.Adaptadores.Grpc.Servicios;
using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Aplicacion.Planes;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Contratos.V1;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Integracion.Tests.Host;

/// <summary>Traducción entre el contrato gRPC v1 y Aplicación (sección 8.1).</summary>
public class TraduccionGrpcTests
{
    public static TheoryData<Type, Type> ParesDeEnums => new()
    {
        { typeof(EstadoPlanGrpc), typeof(EstadoPlan) },
        { typeof(EstadoProyectoGrpc), typeof(EstadoProyecto) },
        { typeof(EstadoNecesidadGrpc), typeof(EstadoNecesidad) },
        { typeof(TipoAlertaGrpc), typeof(TipoAlerta) },
        { typeof(EstadoAlertaGrpc), typeof(EstadoAlerta) },
        { typeof(TipoDocumentoGrpc), typeof(TipoDocumento) },
        { typeof(TipoProyectoGrpc), typeof(TipoProyecto) },
        { typeof(EstadoCambioPresupuestalGrpc), typeof(EstadoCambioPresupuestal) },
        { typeof(RolGrpc), typeof(Rol) },
        { typeof(ResultadoOperacionGrpc), typeof(ResultadoOperacion) },
        { typeof(MotivoEvaluacionNoAceptadaGrpc), typeof(MotivoEvaluacionNoAceptada) },
        { typeof(RespuestaFinCaracterizacionGrpc), typeof(RespuestaFinCaracterizacion) },
        { typeof(TipoCambioEvaluacionGrpc), typeof(TipoCambioEvaluacion) },
        { typeof(UbicacionAlertaGrpc), typeof(UbicacionAlerta) }
    };

    /// <summary>
    /// Cada enum gRPC tiene el valor 0 "no especificado" y luego los mismos valores, en el mismo
    /// orden, que su par en Aplicación o Dominio. Si alguien cambia uno sin el otro, esto falla.
    /// </summary>
    [Theory]
    [MemberData(nameof(ParesDeEnums))]
    public void Los_enums_gRPC_estan_alineados_con_Aplicacion_y_Dominio(Type grpc, Type aplicacion)
    {
        // protoc conserva el prefijo del valor (TIPO_PROYECTO_RECUPERACION -> TipoProyectoRecuperacion).
        var valoresGrpc = Enum.GetValues(grpc).Cast<Enum>().ToList();
        var valoresAplicacion = Enum.GetValues(aplicacion).Cast<Enum>().ToList();

        Assert.Equal(valoresAplicacion.Count + 1, valoresGrpc.Count);
        Assert.EndsWith("NoEspecificado", valoresGrpc[0].ToString().Replace("NoEspecificada", "NoEspecificado"));
        Assert.All(valoresAplicacion, valor =>
        {
            var par = valoresGrpc.Single(g => Convert.ToInt32(g) == Convert.ToInt32(valor) + 1);
            Assert.EndsWith(valor.ToString(), par.ToString());
        });
    }

    [Fact]
    public void Un_enum_no_especificado_es_un_dato_invalido()
    {
        var ex = Assert.Throws<RpcException>(() => TraduccionGrpc.Enum<TipoProyecto>((TipoProyectoGrpc)0, "tipo"));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Theory]
    [InlineData("1500.50", 1500.50)]
    [InlineData("-200.00", -200.00)]
    public void Los_montos_se_leen_como_decimal_exacto(string texto, double esperado) =>
        Assert.Equal((decimal)esperado, TraduccionGrpc.Decimal(texto, "monto"));

    [Theory]
    [InlineData("1,500.50")]
    [InlineData("1500,50")]
    [InlineData("1e3")]
    [InlineData("")]
    public void Un_monto_mal_formado_es_INVALID_ARGUMENT(string texto) =>
        Assert.Equal(StatusCode.InvalidArgument, Assert.Throws<RpcException>(() => TraduccionGrpc.Decimal(texto, "monto")).StatusCode);

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("01/02/2026")]
    public void Una_fecha_mal_formada_es_INVALID_ARGUMENT(string texto) =>
        Assert.Equal(StatusCode.InvalidArgument, Assert.Throws<RpcException>(() => TraduccionGrpc.Fecha(texto, "fecha")).StatusCode);

    [Theory]
    [InlineData("no-es-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Un_identificador_invalido_o_vacio_es_INVALID_ARGUMENT(string texto) =>
        Assert.Equal(StatusCode.InvalidArgument, Assert.Throws<RpcException>(() => TraduccionGrpc.Id(texto, "id")).StatusCode);

    [Theory]
    [InlineData(CodigoError.DatosInvalidos, StatusCode.InvalidArgument)]
    [InlineData(CodigoError.ReglaIncumplida, StatusCode.FailedPrecondition)]
    [InlineData(CodigoError.ConflictoVersion, StatusCode.Aborted)]
    [InlineData(CodigoError.NoAutorizado, StatusCode.PermissionDenied)]
    [InlineData(CodigoError.SolicitudRepetidaConDatosDistintos, StatusCode.AlreadyExists)]
    [InlineData(CodigoError.NoEncontrado, StatusCode.NotFound)]
    public void Cada_codigo_de_error_tiene_su_estado_gRPC(CodigoError codigo, StatusCode esperado) =>
        Assert.Equal(esperado, TraduccionGrpc.Codigo(codigo));

    [Fact]
    public void La_regla_incumplida_viaja_en_los_trailers()
    {
        var resultado = Resultado<ResultadoComando>.Fallo(CodigoError.ReglaIncumplida, "Falta el acta de entrega.", "RN-RE-12");

        var ex = Assert.Throws<RpcException>(() => TraduccionGrpc.Valor(resultado));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Equal("Falta el acta de entrega.", ex.Status.Detail);
        Assert.Equal("RN-RE-12", ex.Trailers.GetValue(TraduccionGrpc.TrailerRegla));
    }

    [Fact]
    public void Los_decimales_salen_con_dos_cifras_y_punto() =>
        Assert.Equal("1500.50", TraduccionGrpc.Texto(1500.5m));
}
