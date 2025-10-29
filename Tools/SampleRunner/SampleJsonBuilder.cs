using System.Collections.Generic;

namespace Tools.SampleRunner.Services
{
    public interface ITypeSchemaProvider
    {
        TypeSchema? GetSchema(string typeName);
        System.Collections.Generic.IEnumerable<string> GetTypeNames();
        System.Collections.Generic.IEnumerable<string>? GetEnumValues(string typeName);
    }

    public sealed class TypeSchema
    {
        public string Name { get; init; } = "";
        public List<Field> Fields { get; init; } = new();

        public sealed class Field
        {
            public string Name { get; init; } = "";
            public string Kind { get; init; } = ""; // int,double,string,bool,enum,struct,sequence
            public string? Type { get; init; }
            public string? NestedType { get; init; }
            public string? SequenceElementType { get; init; }
            public int? MaxLen { get; init; }
            public int? UpperBound { get; init; }
            public List<string>? EnumValues { get; init; }
            public bool IsSequenceOfPrimitive { get; init; }
            public bool IsSequenceOfString { get; init; }
            public bool IsKey { get; init; }
            public bool IsArray => Kind == "sequence";
        }
    }

    public sealed class SampleJsonBuilder
    {
        private readonly ITypeSchemaProvider _types;
        public SampleJsonBuilder(ITypeSchemaProvider types) => _types = types;

        public object BuildSample(string typeName)
        {
            var schema = _types.GetSchema(typeName);
            if (schema == null)
            {
                var k = BasicKindFrom(typeName);
                return k switch
                {
                    "int" => 0,
                    "double" => 0.0,
                    "bool" => false,
                    "string" => "",
                    _ => new System.Collections.Generic.Dictionary<string, object?>()
                };
            }

            var dict = new Dictionary<string, object?>();
            foreach (var f in schema.Fields)
            {
                dict[f.Name] = SampleForField(f);
            }
            return dict;
        }

        private object? SampleForField(TypeSchema.Field f)
        {
            switch (f.Kind)
            {
                case "int": return 1;
                case "double": return 1.0;
                case "bool": return false;
                case "string":
                    {
                        if (!string.IsNullOrWhiteSpace(f.Name) && f.Name.StartsWith("A_")) return f.Name.Substring(2);
                        return f.Name ?? string.Empty;
                    }
                case "enum": return (f.EnumValues?.Count ?? 0) > 0 ? f.EnumValues![0] : "";
                case "struct":
                    if (!string.IsNullOrWhiteSpace(f.NestedType))
                    {
                        var nestedSchema = _types.GetSchema(f.NestedType!);
                        if (nestedSchema != null) return BuildSample(f.NestedType!);
                        var enumVals = _types.GetEnumValues(f.NestedType!);
                        if (enumVals != null)
                        {
                            foreach (var v in enumVals) if (!string.IsNullOrWhiteSpace(v)) return v;
                        }
                        var kb = BasicKindFrom(f.NestedType);
                        return kb switch
                        {
                            "int" => 1,
                            "double" => 1.0,
                            "bool" => false,
                            "string" => (!string.IsNullOrWhiteSpace(f.Name) && f.Name.StartsWith("A_")) ? f.Name.Substring(2) : (f.Name ?? string.Empty),
                            _ => new System.Collections.Generic.Dictionary<string, object?>()
                        };
                    }
                    if (!string.IsNullOrWhiteSpace(f.Type))
                    {
                        var nestedSchema2 = _types.GetSchema(f.Type!);
                        if (nestedSchema2 != null) return BuildSample(f.Type!);
                        var kb2 = BasicKindFrom(f.Type);
                        return kb2 switch
                        {
                            "int" => 1,
                            "double" => 1.0,
                            "bool" => false,
                            "string" => (!string.IsNullOrWhiteSpace(f.Name) && f.Name.StartsWith("A_")) ? f.Name.Substring(2) : (f.Name ?? string.Empty),
                            _ => new System.Collections.Generic.Dictionary<string, object?>()
                        };
                    }
                    return new System.Collections.Generic.Dictionary<string, object?>();
                case "sequence":
                    if (f.IsSequenceOfString)
                    {
                        if (!string.IsNullOrWhiteSpace(f.Name) && f.Name.StartsWith("A_")) return new List<object?> { f.Name.Substring(2) };
                        return new List<object?> { f.Name ?? string.Empty };
                    }
                    if (f.IsSequenceOfPrimitive && !string.IsNullOrWhiteSpace(f.SequenceElementType))
                    {
                        var k = BasicKindFrom(f.SequenceElementType);
                        var elemName = f.SequenceElementType ?? "item";
                        var elem = new TypeSchema.Field { Name = elemName, Kind = k, Type = f.SequenceElementType };
                        return new List<object?> { SampleForField(elem) };
                    }
                    if (!string.IsNullOrWhiteSpace(f.SequenceElementType))
                    {
                        var nested = f.SequenceElementType;
                        var o = BuildSample(nested!);
                        return new List<object?> { o };
                    }
                    return new List<object?>();
                default: return null;
            }
        }

        private static string BasicKindFrom(string? type)
        {
            if (type is null) return "string";
            var t = type.ToLowerInvariant();
            if (t is "int" or "int32" or "int64") return "int";
            if (t is "double" or "float") return "double";
            if (t is "bool") return "bool";
            if (t.Contains("shortstring") || t.Contains("string") || t.Contains("char")) return "string";
            if (t.Contains("frequency") || t.Contains("hertz") || t.Contains("power") || t.Contains("volume")) return "int";
            if (t.StartsWith("c_")) return "struct";
            return "string";
        }
    }
}
