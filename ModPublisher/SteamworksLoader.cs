using System;
using System.IO;
using System.Reflection;
using Game.Orchestration;
using JetBrains.Annotations;

namespace ModPublisher;

public static class SteamworksLoader
{
    public static void Install()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    [CanBeNull]
    private static Assembly Resolve([CanBeNull] object sender, ResolveEventArgs args)
    {
        // On Posix systems the Steamworks Dll is named differently, so search for that too
        var name = new AssemblyName(args.Name);
        if (name.Name != "Facepunch.Steamworks.Win64") return null;
        var posixPath = Path.Combine(
            Path.GetDirectoryName(typeof(GameOrchestrator).Assembly.Location)!,
            "Facepunch.Steamworks.Posix.dll");

        ModPublisher.Logger.Info!.LogFormat("Changed Steamworks Path: {0}", posixPath);
        return File.Exists(posixPath) ? Assembly.LoadFrom(posixPath) : null;
    }
}