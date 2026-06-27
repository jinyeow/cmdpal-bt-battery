using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace BtBattery.Extension;

/// <summary>Invokable command that does nothing — used for display-only list items.</summary>
internal sealed partial class NoOpCommand : InvokableCommand
{
    public NoOpCommand()
    {
        Name = string.Empty;
    }

    public override ICommandResult Invoke() => CommandResult.KeepOpen();
}
