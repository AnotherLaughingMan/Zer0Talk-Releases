using System;
using System.Runtime.CompilerServices;

namespace ZTalk;

internal static class StartupInit
{
    [ModuleInitializer]
    public static void Init()
    {
        try
        {
            // Earliest marker possible (before Program.Main)
            ZTalk.Utilities.ErrorLogger.LogException(new InvalidOperationException("Startup.ModuleInit"), source: "Trace");
        }
        catch { }
    }
}
