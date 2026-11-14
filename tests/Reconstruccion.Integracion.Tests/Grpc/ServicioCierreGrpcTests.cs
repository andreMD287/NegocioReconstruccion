using Grpc.Core;
using Reconstruccion.Adaptadores.Grpc.Clientes;
using Reconstruccion.Contratos.V1;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Grpc;

public sealed class ServicioCierreGrpcTests
{
    private static readonly Guid Desastre = Guid.NewGuid();

    private static ServicioCierreGrpc Crear(
        CallInvoker invocador,
        int maxIntentos = 3,
        int umbral = 3,
        TimeSpan? apertura = null)
    {
        var configuracion = new ConfiguracionClienteCierreGrpc(
            "http://localhost:5055",
            TimeSpan.FromSeconds(3),
            maxIntentos,
            umbral,
            apertura ?? TimeSpan.FromSeconds(15));

        return new ServicioCierreGrpc(
            new CierreService.CierreServiceClient(invocador),
            new CircuitoCierreGrpc(),
            configuracion);
    }

    [Fact]
    public async Task PlanCompletado_EnviaIdentificadoresYDeadline()
    {
        var llamadas = 0;

        var transporte = new InvocadorSimulado((solicitud, opciones) =>
        {
            llamadas++;

            var req = Assert.IsType<NotificarPlanCompletadoRequest>(
                solicitud);

            Assert.Equal(Desastre.ToString("D"), req.DesastreId);
            Assert.Equal(12, req.VersionPlan);

            Assert.NotNull(opciones.Deadline);

            var restante =
                (opciones.Deadline.Value - DateTime.UtcNow)
                .TotalSeconds;

            Assert.InRange(restante, 0, 3.5);

            return Task.FromResult<object>(
                new RespuestaCierreGrpc { Aceptada = true });
        });

        var respuesta = await Crear(transporte)
            .NotificarPlanCompletadoAsync(Desastre, 12);

        Assert.True(respuesta.Aceptada);
        Assert.Null(respuesta.Motivo);
        Assert.Equal(1, llamadas);
    }

    [Fact]
    public async Task Reapertura_Rechazada_NoSeReintenta()
    {
        var llamadas = 0;

        var transporte = new InvocadorSimulado((solicitud, _) =>
        {
            llamadas++;

            var req = Assert.IsType<NotificarReaperturaRequest>(
                solicitud);

            Assert.Equal(Desastre.ToString("D"), req.DesastreId);
            Assert.Equal(13, req.VersionPlan);

            return Task.FromResult<object>(
                new RespuestaCierreGrpc
                {
                    Aceptada = false,
                    Motivo = "Version rechazada"
                });
        });

        var respuesta = await Crear(transporte)
            .NotificarReaperturaAsync(Desastre, 13);

        Assert.False(respuesta.Aceptada);
        Assert.Equal("Version rechazada", respuesta.Motivo);
        Assert.Equal(1, llamadas);
    }

    [Fact]
    public async Task ErrorTemporal_ReintentaHastaLograrEntrega()
    {
        var llamadas = 0;

        var transporte = new InvocadorSimulado((_, _) =>
        {
            llamadas++;

            if (llamadas < 3)
            {
                throw new RpcException(
                    new Status(StatusCode.Unavailable, "Sin conexion"));
            }

            return Task.FromResult<object>(
                new RespuestaCierreGrpc { Aceptada = true });
        });

        var respuesta = await Crear(transporte)
            .NotificarPlanCompletadoAsync(Desastre, 14);

        Assert.True(respuesta.Aceptada);
        Assert.Equal(3, llamadas);
    }

    [Fact]
    public async Task FallosConsecutivos_AbrenCircuito_YLuegoRecupera()
    {
        var llamadas = 0;
        var disponible = false;

        var transporte = new InvocadorSimulado((_, _) =>
        {
            llamadas++;

            if (!disponible)
            {
                throw new RpcException(
                    new Status(StatusCode.Unavailable, "No disponible"));
            }

            return Task.FromResult<object>(
                new RespuestaCierreGrpc { Aceptada = true });
        });

        var cierre = Crear(
            transporte,
            maxIntentos: 1,
            umbral: 3,
            apertura: TimeSpan.FromMilliseconds(250));

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<RpcException>(
                () => cierre.NotificarPlanCompletadoAsync(
                    Desastre, 15));
        }

        Assert.Equal(3, llamadas);

        var bloqueado = await Assert.ThrowsAsync<RpcException>(
            () => cierre.NotificarPlanCompletadoAsync(
                Desastre, 15));

        Assert.Equal(StatusCode.Unavailable, bloqueado.StatusCode);
        Assert.Contains("abierto", bloqueado.Status.Detail);
        Assert.Equal(3, llamadas);

        disponible = true;

        await Task.Delay(400);

        var recuperado = await cierre.NotificarPlanCompletadoAsync(
            Desastre, 15);

        Assert.True(recuperado.Aceptada);
        Assert.Equal(4, llamadas);
    }

    [Fact]
    public async Task ErrorNoTransitorio_NoReintentaNiAbreCircuito()
    {
        var llamadas = 0;

        var transporte = new InvocadorSimulado((_, _) =>
        {
            llamadas++;

            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "Solicitud invalida"));
        });

        var cierre = Crear(
            transporte,
            maxIntentos: 3,
            umbral: 1);

        for (var i = 0; i < 2; i++)
        {
            var error = await Assert.ThrowsAsync<RpcException>(
                () => cierre.NotificarPlanCompletadoAsync(
                    Desastre, 16));

            Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        }

        Assert.Equal(2, llamadas);
    }

    [Fact]
    public async Task Cancelacion_DetieneReintentos()
    {
        using var cancelacion = new CancellationTokenSource();

        var llamadas = 0;

        var transporte = new InvocadorSimulado((_, _) =>
        {
            llamadas++;
            cancelacion.Cancel();

            throw new RpcException(
                new Status(StatusCode.Unavailable, "Fallo temporal"));
        });

        var cierre = Crear(transporte);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cierre.NotificarPlanCompletadoAsync(
                Desastre,
                17,
                cancelacion.Token));

        Assert.Equal(1, llamadas);
    }

    private sealed class InvocadorSimulado(
        Func<object, CallOptions, Task<object>> ejecutar) : CallInvoker
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<
            TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            async Task<TResponse> ResponderAsync() =>
                (TResponse)await ejecutar(request, options);

            return new AsyncUnaryCall<TResponse>(
                ResponderAsync(),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { });
        }

        public override TResponse BlockingUnaryCall<
            TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request) =>
            throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse>
            AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request) =>
            throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse>
            AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options) =>
            throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse>
            AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options) =>
            throw new NotSupportedException();
    }
}