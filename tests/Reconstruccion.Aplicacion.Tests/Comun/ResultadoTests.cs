using Reconstruccion.Aplicacion.Comun;

namespace Reconstruccion.Aplicacion.Tests.Comun;

public class ResultadoTests
{
    [Fact]
    public void Un_resultado_exitoso_expone_su_valor()
    {
        var resultado = Resultado<ResultadoComando>.Exito(new ResultadoComando(Guid.NewGuid(), 3));

        Assert.True(resultado.EsExitoso);
        Assert.Null(resultado.Error);
        Assert.Equal(3, resultado.Valor.NuevaVersion);
    }

    [Fact]
    public void Un_resultado_fallido_conserva_codigo_y_regla()
    {
        var resultado = Resultado<ResultadoComando>.Fallo(CodigoError.ConflictoVersion, "La versión cambió.", "RN-RE-18");

        Assert.False(resultado.EsExitoso);
        Assert.Equal(CodigoError.ConflictoVersion, resultado.Error!.Codigo);
        Assert.Equal("RN-RE-18", resultado.Error.Regla);
        Assert.Throws<InvalidOperationException>(() => resultado.Valor);
    }

    [Fact]
    public void Un_error_se_convierte_en_resultado_fallido()
    {
        Resultado<ResultadoComando> resultado = new Error(CodigoError.NoAutorizado, "Sin permiso.", "RN-RE-19");

        Assert.False(resultado.EsExitoso);
        Assert.Equal(CodigoError.NoAutorizado, resultado.Error!.Codigo);
    }
}
