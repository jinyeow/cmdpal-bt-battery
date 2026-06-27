using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace BtBattery.Extension;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
        {
            return;
        }

        ComServer server = new();
        server.RegisterClass<BtBatteryExtension, IExtension>();
        server.Start();

        BtBatteryExtension.DisposedEvent.WaitOne();
        server.Stop();
    }
}
