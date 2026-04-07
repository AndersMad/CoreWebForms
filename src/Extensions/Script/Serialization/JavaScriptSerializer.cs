// MIT License.

#nullable enable

using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web.Resources;

namespace System.Web.Script.Serialization;

public class JavaScriptSerializer
{
    internal const string ServerTypeFieldName = "__type";
    internal const int DefaultRecursionLimit = 100;
    internal const int DefaultMaxJsonLength = 2097152;

    private static readonly JavaScriptSerializer _defaultJavaScriptSerializer = new();

    private readonly JavaScriptTypeResolver? _typeResolver;
    private Dictionary<Type, JavaScriptConverter>? _converters;
    private int _recursionLimit = DefaultRecursionLimit;
    private int _maxJsonLength = DefaultMaxJsonLength;

    private readonly JsonSerializerOptions _options = new()
    {
        MaxDepth = DefaultRecursionLimit,
        PropertyNameCaseInsensitive = true,
    };

    public JavaScriptSerializer()
    {
        _options.Converters.Add(new InferredObjectJsonConverter());
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    public JavaScriptSerializer(JavaScriptTypeResolver? resolver)
    {
        _typeResolver = resolver;
        _options.Converters.Add(new InferredObjectJsonConverter());
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    internal static string SerializeInternal(object o) => _defaultJavaScriptSerializer.Serialize(o);

    internal static object? Deserialize(JavaScriptSerializer serializer, string input, Type type, int depthLimit)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Length > serializer.MaxJsonLength)
        {
            throw new ArgumentException(AtlasWeb.JSON_MaxJsonLengthExceeded, nameof(input));
        }

        return JsonSerializer.Deserialize(input, type, serializer._options);
    }

    public int MaxJsonLength
    {
        get => _maxJsonLength;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(AtlasWeb.JSON_InvalidMaxJsonLength);
            }
            _maxJsonLength = value;
        }
    }

    public int RecursionLimit
    {
        get => _recursionLimit;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(AtlasWeb.JSON_InvalidRecursionLimit);
            }
            _recursionLimit = value;
        }
    }

    internal JavaScriptTypeResolver? TypeResolver => _typeResolver;

    private Dictionary<Type, JavaScriptConverter> Converters => _converters ??= [];

    public void RegisterConverters(IEnumerable<JavaScriptConverter> converters)
    {
        ArgumentNullException.ThrowIfNull(converters);

        foreach (var converter in converters)
        {
            _options.Converters.Add(new JavaScriptConverterWrapper(converter, this));

            // Keep the legacy converter map alongside the System.Text.Json wrapper.
            if (converter.SupportedTypes is null)
            {
                continue;
            }

            foreach (var supportedType in converter.SupportedTypes)
            {
                Converters[supportedType] = converter;
            }
        }
    }

    public T? Deserialize<T>(string input) => (T?)Deserialize(this, input, typeof(T), RecursionLimit);

    public object? Deserialize(string input, Type targetType) => Deserialize(this, input, targetType, RecursionLimit);

    public T ConvertToType<T>(object obj) => (T)ObjectConverter.ConvertObjectToType(obj, typeof(T), this);

    public object ConvertToType(object obj, Type targetType) => ObjectConverter.ConvertObjectToType(obj, targetType, this);

    public string Serialize(object obj) => Serialize(obj, SerializationFormat.JSON);

    internal string Serialize(object obj, SerializationFormat serializationFormat)
    {
        StringBuilder sb = new StringBuilder();
        Serialize(obj, sb, serializationFormat);
        return sb.ToString();
    }

    public void Serialize(object obj, StringBuilder output) => Serialize(obj, output, SerializationFormat.JSON);

    internal void Serialize(object obj, StringBuilder output, SerializationFormat serializationFormat)
    {
        // Use the legacy object walker for serialization instead of delegating to
        // System.Text.Json directly. WebForms controls rely on JavaScriptSerializer
        // semantics such as ScriptIgnore, custom converters, and special handling for
        // script descriptors; otherwise it can serialize deep server-side graphs.
        SerializeValue(obj, output, 0, null, serializationFormat);

        if (serializationFormat == SerializationFormat.JSON && output.Length > MaxJsonLength)
        {
            throw new InvalidOperationException(AtlasWeb.JSON_MaxJsonLengthExceeded);
        }
    }

    internal bool ConverterExistsForType(Type type, [MaybeNullWhen(false)] out JavaScriptConverter converter)
    {
        converter = GetConverter(type);
        return converter is not null;
    }

    private JavaScriptConverter? GetConverter(Type? type)
    {
        while (type is not null)
        {
            // Match the .NET Framework implementation by walking the inheritance chain.
            // This is required for controls that register a converter for a base type but
            // later serialize a derived runtime instance.
            if (_converters is not null && _converters.TryGetValue(type, out var converter))
            {
                return converter;
            }

            type = type.BaseType;
        }

        return null;
    }

    private static void SerializeBoolean(bool value, StringBuilder output)
        => output.Append(value ? "true" : "false");

    private static void SerializeUri(Uri uri, StringBuilder output)
        => output.Append('"').Append(uri.GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped)).Append('"');

    private static void SerializeGuid(Guid guid, StringBuilder output)
        => output.Append('"').Append(guid.ToString()).Append('"');

    internal static readonly long DatetimeMinTimeTicks = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    private static void SerializeDateTime(DateTime value, StringBuilder output, SerializationFormat serializationFormat)
    {
        if (serializationFormat == SerializationFormat.JSON)
        {
            output.Append("\"\\/Date(");
            output.Append((value.ToUniversalTime().Ticks - DatetimeMinTimeTicks) / 10000);
            output.Append(")\\/\"");
            return;
        }

        output.Append("new Date(");
        output.Append((value.ToUniversalTime().Ticks - DatetimeMinTimeTicks) / 10000);
        output.Append(')');
    }

    private void SerializeCustomObject(object value, StringBuilder output, int depth, Hashtable? objectsInUse, SerializationFormat serializationFormat)
    {
        var first = true;
        var type = value.GetType();

        output.Append('{');

        if (TypeResolver is not null)
        {
            var typeString = TypeResolver.ResolveTypeId(type);
            if (typeString is not null)
            {
                SerializeString(ServerTypeFieldName, output);
                output.Append(':');
                SerializeValue(typeString, output, depth, objectsInUse, serializationFormat);
                first = false;
            }
        }

        foreach (var fieldInfo in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (CheckScriptIgnoreAttribute(fieldInfo))
            {
                continue;
            }

            if (!first)
            {
                output.Append(',');
            }

            SerializeString(fieldInfo.Name, output);
            output.Append(':');
            SerializeValue(fieldInfo.GetValue(value), output, depth, objectsInUse, serializationFormat, fieldInfo);
            first = false;
        }

        foreach (var propertyInfo in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty))
        {
            if (CheckScriptIgnoreAttribute(propertyInfo))
            {
                continue;
            }

            var getMethod = propertyInfo.GetGetMethod();
            if (getMethod is null || getMethod.GetParameters().Length > 0)
            {
                continue;
            }

            if (!first)
            {
                output.Append(',');
            }

            SerializeString(propertyInfo.Name, output);
            output.Append(':');
            SerializeValue(getMethod.Invoke(value, null), output, depth, objectsInUse, serializationFormat, propertyInfo);
            first = false;
        }

        output.Append('}');
    }

    private static bool CheckScriptIgnoreAttribute(MemberInfo memberInfo)
    {
        if (memberInfo.IsDefined(typeof(ScriptIgnoreAttribute), true))
        {
            return true;
        }

        var scriptIgnore = Attribute.GetCustomAttribute(memberInfo, typeof(ScriptIgnoreAttribute), true) as ScriptIgnoreAttribute;
        return scriptIgnore?.ApplyToOverrides == true;
    }

    private void SerializeDictionary(IDictionary value, StringBuilder output, int depth, Hashtable? objectsInUse, SerializationFormat serializationFormat)
    {
        output.Append('{');

        var isFirstElement = true;
        var isTypeEntrySet = false;

        if (value.Contains(ServerTypeFieldName))
        {
            isFirstElement = false;
            isTypeEntrySet = true;
            SerializeDictionaryKeyValue(ServerTypeFieldName, value[ServerTypeFieldName], output, depth, objectsInUse, serializationFormat);
        }

        foreach (DictionaryEntry entry in value)
        {
            if (entry.Key is not string key)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, AtlasWeb.JSON_DictionaryTypeNotSupported, value.GetType().FullName));
            }

            if (isTypeEntrySet && string.Equals(key, ServerTypeFieldName, StringComparison.Ordinal))
            {
                isTypeEntrySet = false;
                continue;
            }

            if (!isFirstElement)
            {
                output.Append(',');
            }

            SerializeDictionaryKeyValue(key, entry.Value, output, depth, objectsInUse, serializationFormat);
            isFirstElement = false;
        }

        output.Append('}');
    }

    private void SerializeDictionaryKeyValue(string key, object? value, StringBuilder output, int depth, Hashtable? objectsInUse, SerializationFormat serializationFormat)
    {
        SerializeString(key, output);
        output.Append(':');
        SerializeValue(value, output, depth, objectsInUse, serializationFormat);
    }

    private void SerializeEnumerable(IEnumerable value, StringBuilder output, int depth, Hashtable? objectsInUse, SerializationFormat serializationFormat)
    {
        output.Append('[');

        var isFirstElement = true;
        foreach (var item in value)
        {
            if (!isFirstElement)
            {
                output.Append(',');
            }

            SerializeValue(item, output, depth, objectsInUse, serializationFormat);
            isFirstElement = false;
        }

        output.Append(']');
    }

    private static void SerializeString(string input, StringBuilder output)
    {
        output.Append('"');
        output.Append(HttpUtility.JavaScriptStringEncode(input));
        output.Append('"');
    }

    private void SerializeValue(object? value, StringBuilder output, int depth, Hashtable? objectsInUse, SerializationFormat serializationFormat, MemberInfo? currentMember = null)
    {
        if (++depth > _recursionLimit)
        {
            throw new ArgumentException(AtlasWeb.JSON_DepthLimitExceeded);
        }

        if (value is not null && ConverterExistsForType(value.GetType(), out var converter))
        {
            // Preserve legacy converter behavior before any generic reflection-based
            // serialization kicks in. This avoids walking into framework objects such as
            // Encoding/HttpResponse that were never meant to be serialized for the client.
            var dictionary = converter.Serialize(value, this);
            if (TypeResolver is not null)
            {
                var typeString = TypeResolver.ResolveTypeId(value.GetType());
                if (typeString is not null)
                {
                    dictionary[ServerTypeFieldName] = typeString;
                }
            }

            output.Append(Serialize(dictionary, serializationFormat));
            return;
        }

        SerializeValueInternal(value, output, depth, objectsInUse, serializationFormat, currentMember);
    }

    private sealed class ReferenceComparer : IEqualityComparer
    {
        bool IEqualityComparer.Equals(object? x, object? y) => ReferenceEquals(x, y);

        int IEqualityComparer.GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private void SerializeValueInternal(object? value, StringBuilder output, int depth, Hashtable? objectsInUse, SerializationFormat serializationFormat, MemberInfo? currentMember)
    {
        if (value is null || DBNull.Value.Equals(value))
        {
            output.Append("null");
            return;
        }

        if (value is string stringValue)
        {
            SerializeString(stringValue, output);
            return;
        }

        if (value is char charValue)
        {
            if (charValue == '\0')
            {
                output.Append("null");
                return;
            }

            SerializeString(charValue.ToString(), output);
            return;
        }

        if (value is bool boolValue)
        {
            SerializeBoolean(boolValue, output);
            return;
        }

        if (value is DateTime dateTime)
        {
            SerializeDateTime(dateTime, output, serializationFormat);
            return;
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            SerializeDateTime(dateTimeOffset.UtcDateTime, output, serializationFormat);
            return;
        }

        if (value is Guid guid)
        {
            SerializeGuid(guid, output);
            return;
        }

        if (value is Uri uri)
        {
            SerializeUri(uri, output);
            return;
        }

        if (value is double doubleValue)
        {
            output.Append(doubleValue.ToString("r", CultureInfo.InvariantCulture));
            return;
        }

        if (value is float floatValue)
        {
            output.Append(floatValue.ToString("r", CultureInfo.InvariantCulture));
            return;
        }

        if (value.GetType().IsPrimitive || value is decimal)
        {
            if (value is IConvertible convertible)
            {
                output.Append(convertible.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                Debug.Assert(false);
                output.Append(value.ToString());
            }

            return;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            var underlyingType = Enum.GetUnderlyingType(type);
            if (underlyingType == typeof(long) || underlyingType == typeof(ulong))
            {
                var errorMessage = currentMember is not null
                    ? string.Format(CultureInfo.CurrentCulture, AtlasWeb.JSON_CannotSerializeMemberGeneric, currentMember.Name, currentMember.ReflectedType?.FullName ?? currentMember.DeclaringType?.FullName ?? string.Empty) + " " + AtlasWeb.JSON_InvalidEnumType
                    : AtlasWeb.JSON_InvalidEnumType;

                throw new InvalidOperationException(errorMessage);
            }

            output.Append(((Enum)value).ToString("D"));
            return;
        }

        try
        {
            if (objectsInUse is null)
            {
                objectsInUse = new Hashtable(new ReferenceComparer());
            }
            else if (objectsInUse.ContainsKey(value))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, AtlasWeb.JSON_CircularReference, type.FullName));
            }

            objectsInUse.Add(value, null);

            if (value is IDictionary dictionary)
            {
                SerializeDictionary(dictionary, output, depth, objectsInUse, serializationFormat);
                return;
            }

            if (value is IEnumerable enumerable)
            {
                SerializeEnumerable(enumerable, output, depth, objectsInUse, serializationFormat);
                return;
            }

            SerializeCustomObject(value, output, depth, objectsInUse, serializationFormat);
        }
        finally
        {
            objectsInUse?.Remove(value);
        }
    }

    internal enum SerializationFormat
    {
        JSON,
        JavaScript
    }

    private sealed class JavaScriptConverterWrapper(JavaScriptConverter converter, JavaScriptSerializer serializer) : JsonConverter<object>
    {
        private readonly HashSet<Type> _types = [.. converter.SupportedTypes ?? Array.Empty<Type>()];

        public JavaScriptConverter Converter => converter;

        public override bool CanConvert(Type typeToConvert)
        {
            foreach (var type in _types)
            {
                if (type.IsAssignableFrom(typeToConvert))
                {
                    return true;
                }
            }

            return false;
        }

        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = ReadDictionary(ref reader, options);
            return converter.Deserialize(result, typeToConvert, serializer);
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, converter.Serialize(value, serializer), options);

        private static object? ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            // https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/converters-how-to#deserialize-inferred-types-to-object-properties
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    if (reader.TryGetDateTime(out var date))
                    {
                        return date;
                    }
                    return reader.GetString();
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.Number:
                    return ReadNumber(ref reader);
                case JsonTokenType.StartObject:
                    return ReadDictionary(ref reader, options);
                case JsonTokenType.StartArray:
                    var list = new ArrayList();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        list.Add(ReadObject(ref reader, options));
                    }
                    return list;
                default:
                    throw new JsonException($"'{reader.TokenType}' is not supported");
            }
        }

        private static object ReadNumber(ref Utf8JsonReader reader)
        {
            if (reader.TryGetInt32(out var int32))
            {
                return int32;
            }

            if (reader.TryGetInt64(out var int64))
            {
                return int64;
            }

            return reader.GetDecimal();
        }

        private static Dictionary<string, object> ReadDictionary(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            var dictionary = new Dictionary<string, object>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return dictionary;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("JsonTokenType was not PropertyName");
                }

                var propertyName = reader.GetString();

                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    throw new JsonException("Failed to get property name");
                }

                reader.Read();

                if (ReadObject(ref reader, options) is { } value)
                {
                    dictionary.Add(propertyName, value);
                }
            }

            return dictionary;
        }
    }

    private sealed class InferredObjectJsonConverter : JsonConverter<object>
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(object);

        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => ReadObject(ref reader);

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }

        private static object? ReadObject(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.Number:
                    return ReadNumber(ref reader);
                case JsonTokenType.StartObject:
                    return ReadDictionary(ref reader);
                case JsonTokenType.StartArray:
                    var list = new ArrayList();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        list.Add(ReadObject(ref reader));
                    }
                    return list;
                default:
                    throw new JsonException($"'{reader.TokenType}' is not supported");
            }
        }

        private static object ReadNumber(ref Utf8JsonReader reader)
        {
            if (reader.TryGetInt32(out var int32))
            {
                return int32;
            }

            if (reader.TryGetInt64(out var int64))
            {
                return int64;
            }

            return reader.GetDecimal();
        }

        private static Dictionary<string, object> ReadDictionary(ref Utf8JsonReader reader)
        {
            var dictionary = new Dictionary<string, object>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return dictionary;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("JsonTokenType was not PropertyName");
                }

                var propertyName = reader.GetString();

                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    throw new JsonException("Failed to get property name");
                }

                reader.Read();

                if (ReadObject(ref reader) is { } value)
                {
                    dictionary.Add(propertyName, value);
                }
            }

            return dictionary;
        }
    }
}
