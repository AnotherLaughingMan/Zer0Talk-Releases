using System;
using System.Runtime.CompilerServices;

namespace Zer0Talk;

internal static class StartupInit
{
    [ModuleInitializer]
    public static void Init()
    {
        try
        {
            // Earliest marker possible (before Program.Main)
            Zer0Talk.Utilities.ErrorLogger.LogException(new InvalidOperationException("Startup.ModuleInit"), source: "Trace");
        }
        catch { }
    }
}
