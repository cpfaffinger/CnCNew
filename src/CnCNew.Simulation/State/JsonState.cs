using System.Text.Json;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.State;

public sealed class JsonStateWriter(Utf8JsonWriter writer) : IStateWriter
{
    public void BeginObject(string? name)
    {
        if (name is null) writer.WriteStartObject(); else writer.WriteStartObject(name);
    }

    public void EndObject() => writer.WriteEndObject();
    public void BeginArray(string name) => writer.WriteStartArray(name);
    public void EndArray() => writer.WriteEndArray();
    public void Bool(string name, bool value) => writer.WriteBoolean(name, value);
    public void Int(string name, long value) => writer.WriteNumber(name, value);
    public void UInt(string name, ulong value) => writer.WriteNumber(name, value);
    public void Fixed(string name, Fixed value) => writer.WriteNumber(name, value.Raw);
    public void Text(string name, string value) => writer.WriteString(name, value);
}

public sealed class JsonStateReader(JsonElement element) : IStateReader
{
    public bool Has(string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out _);

    public bool Bool(string name) => Get(name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new InvalidDataException($"State field '{name}' is not a boolean."),
    };

    public long Int(string name) => Get(name).TryGetInt64(out var value) ? value : throw new InvalidDataException($"State field '{name}' is not an integer.");
    public ulong UInt(string name) => Get(name).TryGetUInt64(out var value) ? value : throw new InvalidDataException($"State field '{name}' is not an unsigned integer.");
    public Fixed Fixed(string name) => Numerics.Fixed.FromRaw(Int(name));
    public string Text(string name) => Get(name).GetString() ?? throw new InvalidDataException($"State field '{name}' is null.");

    public IStateReader Object(string name)
    {
        var value = Get(name);
        return value.ValueKind == JsonValueKind.Object ? new JsonStateReader(value) : throw new InvalidDataException($"State field '{name}' is not an object.");
    }

    public IReadOnlyList<IStateReader> Array(string name)
    {
        var value = Get(name);
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"State field '{name}' is not an array.");
        return [..value.EnumerateArray().Select(item => (IStateReader)new JsonStateReader(item))];
    }

    private JsonElement Get(string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidDataException($"State field '{name}' is missing.");
}
