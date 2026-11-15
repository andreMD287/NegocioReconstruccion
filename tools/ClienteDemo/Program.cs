using Reconstruccion.ClienteDemo;

// Cliente de demostración: hace de Caracterización y de los usuarios de la plataforma contra
// un Reconstruccion.Host en ejecución. No referencia Dominio ni Aplicación, solo los contratos
// gRPC y SignalR, igual que lo haría un cliente externo real.
//
// Uso:
//   dotnet run --project tools/ClienteDemo -- [https://localhost:7103]
var direccion = args.Length > 0 ? args[0] : "https://localhost:7103";
var baseUri = new Uri(direccion.EndsWith('/') ? direccion : direccion + "/");

using var cancelacion = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancelacion.Cancel();
};

Console.WriteLine($"Reconstruccion.ClienteDemo — conectando a {baseUri}");
Console.WriteLine("El Host debe estar corriendo en Development (expone /dev/token).");

using var http = new HttpClient(new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
})
{ BaseAddress = baseUri };

await using var canal = new CanalReconstruccion(baseUri);

try
{
    await EscenarioPrincipal.EjecutarAsync(baseUri, canal, http, cancelacion.Token);
    await EscenariosFallo.EjecutarAsync(canal, http, cancelacion.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Demostración cancelada.");
    return 1;
}
catch (HttpRequestException ex)
{
    Consola.Error($"No se pudo conectar con el Host en {baseUri}: {ex.Message}");
    return 1;
}

Consola.Titulo("Demostración terminada");
return 0;
