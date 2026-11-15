namespace Reconstruccion.ClienteDemo;

/// <summary>Salida legible de la demostración, sin más propósito que facilitar seguirla en vivo.</summary>
internal static class Consola
{
    public static void Titulo(string texto)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== {texto} ===");
        Console.ResetColor();
    }

    public static void Paso(string texto) => Console.WriteLine($"  -> {texto}");

    public static void Ok(string texto)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  [ok] {texto}");
        Console.ResetColor();
    }

    public static void Error(string texto)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [error] {texto}");
        Console.ResetColor();
    }

    public static void Evento(string texto)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [SignalR] {texto}");
        Console.ResetColor();
    }
}
