// Prosper for PowerToys Command Palette: the COM server Command Palette starts this extension through.
using System;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer.CsWinRT;

namespace ProsperPalette;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();
            ManualResetEvent disposed = new(false);

            // One extension instance for the life of the process: it owns the microphone and the Prosper UI thread.
            ProsperExtension extension = new(disposed);
            server.RegisterClass<ProsperExtension, IExtension>(() => extension);
            server.Start();

            disposed.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("Prosper is a PowerToys Command Palette extension: install the package and open Command Palette.");
        }
    }
}
