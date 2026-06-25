namespace BtBattery.Abstractions;

/// <summary>Whether a device's battery level is a real percentage or absent.</summary>
public enum BatteryState
{
    // Unknown is first (the zero value) so default(BatteryLevel) is the absent reading,
    // never an indistinguishable real 0%.
    Unknown,
    Known,
}

/// <summary>
/// A device battery reading that is either Known (0–100%) or Unknown (property absent).
/// Unknown is a first-class state, distinct from a real 0%.
/// </summary>
public readonly record struct BatteryLevel
{
    private BatteryLevel(BatteryState state, int percent)
    {
        State = state;
        Percent = percent;
    }

    public BatteryState State { get; }

    /// <summary>The percentage when <see cref="State"/> is Known; 0 when Unknown.</summary>
    public int Percent { get; }

    /// <summary>The absent reading. Distinct from <see cref="Known"/>(0).</summary>
    public static readonly BatteryLevel Unknown = new(BatteryState.Unknown, 0);

    /// <summary>Creates a Known reading. Throws if <paramref name="percent"/> is outside 0–100.</summary>
    public static BatteryLevel Known(int percent)
    {
        if (percent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percent), percent, "Battery percent must be between 0 and 100 inclusive.");
        }

        return new BatteryLevel(BatteryState.Known, percent);
    }
}
