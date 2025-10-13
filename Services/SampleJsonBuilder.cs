using System.Collections.Generic;

namespace Agent.UI.Wpf.Services
{
    public interface ITypeSchemaProvider
    {
        TypeSchema? GetSchema(string typeName);
        /// <summary>
        /// 등록된 타입 이름 목록을 반환합니다. (앱 시작시 캐시된 타입을 UI에 채우기 위해 사용)
        /// </summary>
        System.Collections.Generic.IEnumerable<string> GetTypeNames();
        /// <summary>
        /// 주어진 타입이 enum이라면 enumerator 이름 목록을 반환합니다.
        /// </summary>
        System.Collections.Generic.IEnumerable<string>? GetEnumValues(string typeName);
    }

    public sealed class TypeSchema
    {
        public string Name { get; init; } = "";
        public List<Field> Fields { get; init; } = new();

        public sealed class Field
        {
            public string Name { get; init; } = "";
            /// <summary>기본/구조/열거/시퀀스 등의 종류: int,double,string,bool,enum,struct,sequence</summary>
            public string Kind { get; init; } = ""; // int,double,string,bool,enum,struct,sequence
            /// <summary>원시 type 속성 또는 nonBasicTypeName에 들어있는 문자열(모듈 포함 가능)</summary>
            public string? Type { get; init; }
            /// <summary>복합(또는 enum) 타입의 실제 타입명(module::C_Name)</summary>
            public string? NestedType { get; init; }
            /// <summary>시퀀스 원소 타입(프리미티브명 또는 구조체 타입명)</summary>
            public string? SequenceElementType { get; init; }
            /// <summary>문자열 최대 길이</summary>
            public int? MaxLen { get; init; }
            /// <summary>시퀀스 상한(upper bound)</summary>
            public int? UpperBound { get; init; }
            public List<string>? EnumValues { get; init; }
            /// <summary>시퀀스가 primitive 원소로 이루어진 경우</summary>
            public bool IsSequenceOfPrimitive { get; init; }
            /// <summary>시퀀스가 char 기반 문자열(char sequence)인 경우</summary>
            public bool IsSequenceOfString { get; init; }
            /// <summary>key/isKey 속성</summary>
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
                // if there's no schema, try to infer basic kind from the type name (typedef to primitive)
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
                        // derive default string from field name: strip leading A_ if present
                        if (!string.IsNullOrWhiteSpace(f.Name) && f.Name.StartsWith("A_")) return f.Name.Substring(2);
                        return f.Name ?? string.Empty;
                    }
                case "enum": return (f.EnumValues?.Count ?? 0) > 0 ? f.EnumValues![0] : "";
                case "struct":
                    // prefer NestedType (module::T_Name) when present
                    if (!string.IsNullOrWhiteSpace(f.NestedType))
                    {
                        // if nested type has a schema, recurse
                        var nestedSchema = _types.GetSchema(f.NestedType!);
                        if (nestedSchema != null) return BuildSample(f.NestedType!);
                        // if nested type is an enum, try to get enum values from provider
                        var enumVals = _types.GetEnumValues(f.NestedType!);
                        if (enumVals != null)
                        {
                            foreach (var v in enumVals) if (!string.IsNullOrWhiteSpace(v)) return v;
                        }
                        // otherwise infer basic kind
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
                    // string sequence (char sequence) => represent as single string element in array per sample
                    if (f.IsSequenceOfString)
                    {
                        // derive default string from field name
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
                        // struct sequence
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
            // common typedef patterns
            if (t.Contains("shortstring") || t.Contains("string") || t.Contains("char")) return "string";
            if (t.Contains("frequency") || t.Contains("hertz") || t.Contains("power") || t.Contains("volume")) return "int";
            if (t.StartsWith("c_")) return "struct";
            return "string";
        }
    }
}
