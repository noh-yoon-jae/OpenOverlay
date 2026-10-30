namespace OpenOverlay.Service;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        Console.Error.WriteLine("[OpenOverlay backend] Starting JSON-line service.");
        await using var host = new OpenOverlayHost();
        Console.Error.WriteLine("[OpenOverlay backend] Ready.");
        await host.RunAsync();
    }
}
