using System.Globalization;

namespace Ageledger;

/// <summary>
/// An age range as the platform reports it: bounds only, never a birthdate or exact age.
/// A null <see cref="Upper"/> means "and older"; a null <see cref="Lower"/> means "below the lowest gate".
/// </summary>
public readonly record struct AgeRangeBucket(int? Lower, int? Upper)
{
    /// <summary>No range received (both bounds null).</summary>
    public static AgeRangeBucket Unknown { get; } = new(null, null);

    /// <summary>True when at least one bound is present.</summary>
    public bool IsKnown => Lower is not null || Upper is not null;

    /// <summary>
    /// Short label such as "0-12", "13-15" or "18+". This is the only form of the range that is ever written to the decision log.
    /// </summary>
    public string Label
    {
        get
        {
            if (!IsKnown) return "unknown";
            var lo = Lower ?? 0;
            return Upper is { } up
                ? string.Create(CultureInfo.InvariantCulture, $"{lo}-{up}")
                : string.Create(CultureInfo.InvariantCulture, $"{lo}+");
        }
    }

    /// <summary>Returns <see cref="Label"/>.</summary>
    public override string ToString() => Label;
}
