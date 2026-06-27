using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using System;

namespace BtBattery.Extension;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
        {
            Console.WriteLine("Not launched as COM server — exiting.");
            return;
        }

        ComServer server = new();
        server.RegisterClass<BtBatteryExtension, IExtension>();
        server.Start();

        BtBatteryExtension.DisposedEvent.WaitOne();
        server.Stop();
    }
}
