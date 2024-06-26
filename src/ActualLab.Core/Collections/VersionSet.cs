using System.Diagnostics.CodeAnalysis;
using ActualLab.Comparison;
using ActualLab.Internal;

namespace ActualLab.Collections;

#pragma warning disable CA1721

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
public sealed partial record VersionSet(
    [property: JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    IReadOnlyDictionary<Symbol, Version> Items
) {
    public static readonly Version ZeroVersion = new();
    public static readonly ListFormat ListFormat = ListFormat.CommaSeparated;

    private int _hashCode;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public int Count => Items.Count;
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public string Versions => Format();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public int HashCode {
        get {
            if (_hashCode == 0) {
                var hashCode = 0;
                foreach (var (scope, version) in Items)
                    hashCode ^= System.HashCode.Combine(scope.HashCode, version.GetHashCode());
                if (hashCode == 0)
                    hashCode = 1;
                _hashCode = hashCode;
            }
            return _hashCode;
        }
    }

    public Version this[Symbol scope]
        => Items.GetValueOrDefault(scope, ZeroVersion)!;

    // Constructors

    public VersionSet()
        : this(ImmutableDictionary<Symbol, Version>.Empty)
    { }

    public VersionSet(Symbol scope, Version version)
        : this(new Dictionary<Symbol, Version>() { { scope, version } })
    { }

    public VersionSet(Symbol scope, string version)
        : this(new Dictionary<Symbol, Version>() { { scope, VersionExt.Parse(version) } })
    { }

    public VersionSet(params (Symbol Scope, Version Version)[] versions)
        : this(versions.ToDictionary(kv => kv.Scope, kv => kv.Version))
    { }

    public VersionSet(params (Symbol Scope, string Version)[] versions)
        : this(versions.ToDictionary(kv => kv.Scope, kv => VersionExt.Parse(kv.Version)))
    { }

    [Newtonsoft.Json.JsonConstructor, JsonConstructor, MemoryPackConstructor]
    public VersionSet(string? versions)
        : this(Parse(versions).Items)
    { }

    // Conversion

    public override string ToString()
        => $"{nameof(VersionSet)}(\"{Versions}\")";

    public string Format()
    {
        if (Items.Count == 0)
            return "";

        using var formatter = ListFormat.CreateFormatter();
        foreach (var (scope, version) in Items)
            formatter.Append($"{scope.Value}={version.Format()}");
        formatter.AppendEnd();
        return formatter.Output;
    }

    // Equality

    public bool Equals(VersionSet? other)
    {
        if (ReferenceEquals(other, null) || Count != other.Count || HashCode != other.HashCode)
            return false;

        foreach (var (scope, version) in other.Items)
            if (Items.GetValueOrDefault(scope) != version)
                return false;
        return true;
    }

    public override int GetHashCode() => HashCode;

    // Parse and TryParse

    public static VersionSet Parse(string? s, bool ignoreErrors = false)
        => TryParse(s, ignoreErrors, out var result) ? result : throw Errors.Format<VersionSet>(s);

    public static bool TryParse(string? s, [MaybeNullWhen(false)] out VersionSet result)
        => TryParse(s, false, out result);
    public static bool TryParse(string? s, bool ignoreErrors, [MaybeNullWhen(false)] out VersionSet result)
    {
        if (s.IsNullOrEmpty()) {
            result = new VersionSet();
            return true;
        }

        result = null;
        var versions = new Dictionary<Symbol, Version>();
        using var parser = ListFormat.CreateParser(s);
        while (parser.TryParseNext()) {
            var item = parser.Item;
            var equalsIndex = item.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex < 0) {
                if (ignoreErrors)
                    continue;
                return false;
            }
#if NETSTANDARD2_0
            if (!Version.TryParse(item.Substring(equalsIndex + 1), out var version)) {
#else
            if (!Version.TryParse(item.AsSpan(equalsIndex + 1), out var version)) {
#endif
                if (ignoreErrors)
                    continue;
                return false;
            }

            var scope = (Symbol)item[..equalsIndex];
            versions[scope] = version;
        }

        result = new VersionSet(versions);
        return true;
    }
}
