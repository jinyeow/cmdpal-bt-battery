using Microsoft.CommandPalette.Extensions;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BtBattery.Extension;

[Guid("ae2698e1-5166-4448-9582-bdfdf457d95f")]
public sealed partial class BtBatteryExtension : IExtension, IDisposable
{
    // Static event: the ComServer creates one instance via reflection; the Program.Main
    // waits on this to know when it's time to stop.
    internal static readonly ManualResetEvent DisposedEvent = new(false);

    private readonly BtBatteryCommandsProvider _provider = new();

    public object? GetProvider(ProviderType providerType) =>
        providerType == ProviderType.Commands ? _provider : null;

    public void Dispose()
    {
        _provider.Dispose();
        DisposedEvent.Set();
    }
}
