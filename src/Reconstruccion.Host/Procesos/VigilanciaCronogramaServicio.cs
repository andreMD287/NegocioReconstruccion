using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Host.Seguridad;

namespace Reconstruccion.Host.Procesos;

/// <summary>Sección "Vigilancia" de la configuración.</summary>
public sealed class OpcionesVigilancia
{
    public bool Habilitada { get; set; } = true;

    public int IntervaloMinutos { get; set; } = 60;
}

/// <summary>
/// VigilanciaCronograma (sección 4): proceso periódico con rol Sistema que genera las alertas de
/// hitos vencidos (RN-RE-21). Cada ejecución usa su propio alcance y unidad de trabajo; si falla,
/// la siguiente ejecución vuelve a revisar.
/// </summary>
public sealed class VigilanciaCronogramaServicio(
    IServiceScopeFactory alcances,
    OpcionesVigilancia opciones,
    ILogger<VigilanciaCronogramaServicio> registro) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        if (!opciones.Habilitada)
        {
            registro.LogInformation("La vigilancia del cronograma está deshabilitada.");
            return;
        }

        using var temporizador = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, opciones.IntervaloMinutos)));
        do
        {
            await EjecutarUnaVezAsync(detener);
        }
        while (await temporizador.WaitForNextTickAsync(detener));
    }

    /// <summary>Una revisión completa; pública para poder probarla.</summary>
    public async Task<ResultadoVigilanciaCronograma?> EjecutarUnaVezAsync(CancellationToken cancelacion)
    {
        try
        {
            await using var alcance = alcances.CreateAsyncScope();
            alcance.ServiceProvider.GetRequiredService<UsuarioActualAmbito>().ComoSistema();
            var manejador = alcance.ServiceProvider
                .GetRequiredService<IManejadorComando<EjecutarVigilanciaCronograma, ResultadoVigilanciaCronograma>>();

            var resultado = await manejador.ManejarAsync(new EjecutarVigilanciaCronograma(), cancelacion);
            if (resultado.EsExitoso)
            {
                registro.LogInformation("Vigilancia del cronograma: {Proyectos} proyectos revisados, {Alertas} alertas generadas.",
                    resultado.Valor.ProyectosRevisados, resultado.Valor.AlertasGeneradas);
                return resultado.Valor;
            }

            registro.LogWarning("La vigilancia del cronograma no se completó: {Error}", resultado.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registro.LogError(ex, "Error en la vigilancia del cronograma; se reintentará en la siguiente ejecución.");
        }

        return null;
    }
}
