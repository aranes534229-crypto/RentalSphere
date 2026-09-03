namespace RentalSphere.Modules.Equipment.Services;

/// <summary>
/// Builds and sequences EquipmentItem serial numbers. Format: {prefix}{counter:0000},
/// e.g. "TBL-0001", "CHR-0042". Gaps from deleted items are filled by picking the
/// smallest missing counter rather than monotonic increment.
/// </summary>
public static class SerialNumberGenerator
{
    /// <summary>Builds a serial like TBL-0001 from a prefix + counter.</summary>
    public static string Build(string prefix, int counter) =>
        $"{prefix}{counter:0000}";

    /// <summary>
    /// Finds the smallest positive integer not already used by an existing serial
    /// with the same prefix. So if "TBL-0001", "TBL-0002", "TBL-0004" exist, this
    /// returns 3 (filling the gap from a deleted "TBL-0003"). If all of 1..N are
    /// used, returns N+1.
    /// </summary>
    public static int NextCounter(IEnumerable<string> existingSerials, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            throw new ArgumentException("Prefix must be non-empty.", nameof(prefix));

        var used = new HashSet<int>();
        foreach (var s in existingSerials)
        {
            if (s is null || !s.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var tail = s.AsSpan(prefix.Length);
            if (int.TryParse(tail, out var parsed) && parsed > 0)
                used.Add(parsed);
        }

        var candidate = 1;
        while (used.Contains(candidate)) candidate++;
        return candidate;
    }
}
