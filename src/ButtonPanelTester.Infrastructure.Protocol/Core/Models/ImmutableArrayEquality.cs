using System.Collections.Immutable;

namespace Core.Models;

/// <summary>
/// Helper per equality strutturale di <see cref="ImmutableArray{T}"/>.
/// Necessario perché il default <see cref="ImmutableArray{T}.Equals(ImmutableArray{T})"/>
/// confronta la reference dell'array interno, non gli elementi.
/// </summary>
internal static class ImmutableArrayEquality
{
    public static bool SequenceEqual<T>(ImmutableArray<T> a, ImmutableArray<T> b)
        where T : IEquatable<T>
    {
        if (a.IsDefault && b.IsDefault) return true;
        if (a.IsDefault || b.IsDefault) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }
        return true;
    }

    public static int SequenceHash<T>(ImmutableArray<T> a)
        where T : IEquatable<T>
    {
        if (a.IsDefault) return 0;
        var hash = new HashCode();
        hash.Add(a.Length);
        for (int i = 0; i < a.Length; i++)
        {
            hash.Add(a[i]);
        }
        return hash.ToHashCode();
    }
}
