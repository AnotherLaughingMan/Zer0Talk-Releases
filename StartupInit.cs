using System;
using System.Runtime.CompilerServices;

namespace P2PTalk;

internal static class StartupInit
{
    [ModuleInitializer]
    public static void Init()
    {
        try
        {
            // Earliest marker possible (before Program.Main)
            P2PTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("Startup.ModuleInit"), source: "Trace");
        }
        catch { }
    }
}
