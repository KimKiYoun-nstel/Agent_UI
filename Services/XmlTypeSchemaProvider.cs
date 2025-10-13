using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Agent.UI.Wpf.Services
{
    /// <summary>
    /// 간단한 XML 타입 스키마 제공자. config/generated/*.xml 파일에서 struct와 그 멤버를 파싱하여 TypeSchema를 반환합니다.
    /// 이 구현은 파일 형식에 따라 조정이 필요할 수 있습니다.
    /// </summary>
    public sealed class XmlTypeSchemaProvider : ITypeSchemaProvider
    {
        private readonly Dictionary<string, TypeSchema> _cache = new(StringComparer.Ordinal);
        private sealed class TypedefInfo
        {
            public string Underlying { get; init; } = ""; // underlying type name or nonBasicTypeName
            public int? SequenceMax { get; init; }
            public int? StringMax { get; init; }
        }

    private readonly Dictionary<string, TypedefInfo> _typedefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _enums = new(StringComparer.Ordinal);

        public XmlTypeSchemaProvider(string configRoot)
        {
            try
            {
                var gen = Path.Combine(configRoot ?? string.Empty, "generated");
                if (!Directory.Exists(gen)) return;
                var files = Directory.GetFiles(gen, "*.xml");
                foreach (var f in files)
                {
                    try
                    {
                        var doc = XDocument.Load(f);
                        // collect typedefs: <typedef name="T_X" type="int32" nonBasicTypeName="..." sequenceMaxLength="..." />
                        var tdefs = doc.Descendants().Where(e => e.Name.LocalName == "typedef");
                        foreach (var td in tdefs)
                        {
                            var tname = td.Attribute("name")?.Value;
                            var typeAttr = td.Attribute("type")?.Value ?? td.Element("type")?.Value;
                            var nonBasic = td.Attribute("nonBasicTypeName")?.Value ?? td.Element("nonBasicTypeName")?.Value;
                            var seqMax = td.Attribute("sequenceMaxLength")?.Value ?? td.Element("sequenceMaxLength")?.Value;
                            var strMax = td.Attribute("stringMaxLength")?.Value ?? td.Element("stringMaxLength")?.Value;
                            if (string.IsNullOrWhiteSpace(tname)) continue;
                            // underlying: prefer nonBasicTypeName when present, otherwise the type attribute
                            var underlying = !string.IsNullOrWhiteSpace(nonBasic) ? nonBasic : typeAttr ?? "";
                            int? smax = null;
                            if (int.TryParse(seqMax, out var sm)) smax = sm;
                            int? strm = null;
                            if (int.TryParse(strMax, out var stm)) strm = stm;
                            _typedefs[tname] = new TypedefInfo { Underlying = underlying, SequenceMax = smax, StringMax = strm };
                            // also store short name without module prefix
                            var idx = tname.IndexOf("::");
                            if (idx >= 0)
                            {
                                var shortn = tname.Substring(idx + 2);
                                if (!_typedefs.ContainsKey(shortn)) _typedefs[shortn] = _typedefs[tname];
                            }
                        }
                        var structs = doc.Descendants().Where(e => e.Name.LocalName == "struct");
                        // collect enums: <enum name="T_X"> <enumerator name="L_..."/> ...
                        var enums = doc.Descendants().Where(e => e.Name.LocalName == "enum");
                        foreach (var en in enums)
                        {
                            var ename = en.Attribute("name")?.Value;
                            if (string.IsNullOrWhiteSpace(ename)) continue;
                            var vals = en.Elements().Where(x => x.Name.LocalName == "enumerator" || x.Name.LocalName == "value" || x.Name.LocalName == "literal").Select(x => x.Attribute("name")?.Value ?? x.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                            if (vals.Count > 0)
                            {
                                _enums[ename] = vals;
                                var idx = ename.IndexOf("::");
                                if (idx >= 0)
                                {
                                    var shortn = ename.Substring(idx + 2);
                                    if (!_enums.ContainsKey(shortn)) _enums[shortn] = vals;
                                }
                            }
                        }
                        foreach (var s in structs)
                        {
                            var name = s.Attribute("name")?.Value;
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            // Build fields by looking for child 'member' or 'field' elements
                            var fields = new List<TypeSchema.Field>();
                            var members = s.Elements().Where(e => e.Name.LocalName == "member" || e.Name.LocalName == "field" || e.Name.LocalName == "element");
                            foreach (var m in members)
                            {
                                var fname = m.Attribute("name")?.Value;
                                var ftype = m.Attribute("type")?.Value ?? m.Element("type")?.Value;
                                var nonBasic = m.Attribute("nonBasicTypeName")?.Value ?? m.Element("nonBasicTypeName")?.Value;
                                var seqMax = m.Attribute("sequenceMaxLength")?.Value ?? m.Element("sequenceMaxLength")?.Value;
                                var strMax = m.Attribute("stringMaxLength")?.Value ?? m.Element("stringMaxLength")?.Value;
                                var isKeyAttr = m.Attribute("key")?.Value ?? m.Attribute("isKey")?.Value;

                                // Determine effective type string (prefer nonBasic when present)
                                var effectiveType = !string.IsNullOrWhiteSpace(nonBasic) ? nonBasic : ftype;

                                // resolve typedefs (follow typedef chain up to a depth)
                                string? resolvedType = null;
                                TypedefInfo? tdInfo = null;
                                if (!string.IsNullOrWhiteSpace(effectiveType))
                                {
                                    ResolveTypedef(effectiveType, out resolvedType, out tdInfo);
                                }
                                // if resolvedType present use it, otherwise keep effectiveType
                                var finalType = resolvedType ?? effectiveType;

                                // enum values
                                List<string>? enumVals = null;
                                if (m.Elements().Any(x => x.Name.LocalName == "enum" || x.Name.LocalName == "enumerator"))
                                {
                                    enumVals = m.Elements().Where(x => x.Name.LocalName == "enum" || x.Name.LocalName == "enumerator").Select(x => x.Value).Where(v => v != null).ToList();
                                }

                                if (string.IsNullOrWhiteSpace(fname)) continue;

                                // Prepare field properties
                                string kind = "string";
                                string? typeVal = ftype;
                                string? nestedType = null;
                                string? seqElemType = null;
                                int? upperBound = null;
                                int? maxLen = null;
                                bool isSeqPrim = false;
                                bool isSeqStr = false;
                                bool isKey = false;

                                if (int.TryParse(seqMax, out var u)) upperBound = u;
                                if (int.TryParse(strMax, out var ml)) maxLen = ml;
                                if (!string.IsNullOrWhiteSpace(isKeyAttr)) isKey = true;

                                if (string.IsNullOrWhiteSpace(finalType))
                                {
                                    kind = "string";
                                }
                                else
                                {
                                    // consider typedef metadata (tdInfo) for sequence/string
                                    var isSequence = tdInfo?.SequenceMax != null || !string.IsNullOrWhiteSpace(seqMax) || (finalType?.EndsWith("[]") ?? false) || (finalType?.StartsWith("sequence<") ?? false) || finalType?.Contains("sequence") == true;
                                    if (isSequence)
                                    {
                                        kind = "sequence";
                                        typeVal = finalType ?? effectiveType;
                                        // element type extraction
                                        var elem = finalType!;
                                        if (elem.EndsWith("[]")) elem = elem.Substring(0, elem.Length - 2);
                                        var sidx = elem.IndexOf("<");
                                        var eidx = elem.IndexOf(">");
                                        if (sidx >= 0 && eidx > sidx) elem = elem.Substring(sidx + 1, eidx - sidx - 1);
                                        seqElemType = elem;
                                        // if typedef declares string max or element 'char' -> treat as string sequence
                                        if (elem.Equals("char", StringComparison.OrdinalIgnoreCase) || tdInfo?.Underlying.Equals("P_LDM_Common::T_Char", StringComparison.OrdinalIgnoreCase) == true || tdInfo?.Underlying.Equals("T_Char", StringComparison.OrdinalIgnoreCase) == true || tdInfo?.StringMax != null)
                                        {
                                            isSeqStr = true;
                                            isSeqPrim = false;
                                            seqElemType = "char";
                                        }
                                        else
                                        {
                                            var k = InferKindFromType(elem);
                                            if (k == "int" || k == "double" || k == "bool" || k == "string")
                                            {
                                                isSeqPrim = true;
                                                isSeqStr = (k == "string");
                                            }
                                            else
                                            {
                                                // struct sequence
                                                nestedType = elem;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        var k = InferKindFromType(finalType);
                                        if (k == "enum" || (enumVals != null && enumVals.Count > 0))
                                        {
                                            kind = "enum";
                                            nestedType = effectiveType;
                                        }
                                        else if (k == "struct")
                                        {
                                            kind = "struct";
                                            nestedType = finalType;
                                        }
                                        else if (k == "string")
                                        {
                                            kind = "string";
                                        }
                                        else
                                        {
                                            kind = k;
                                        }
                                        typeVal = finalType ?? effectiveType;
                                    }
                                }

                                var newField = new TypeSchema.Field
                                {
                                    Name = fname,
                                    Kind = kind,
                                    Type = typeVal,
                                    NestedType = nestedType,
                                    SequenceElementType = seqElemType,
                                    UpperBound = upperBound,
                                    MaxLen = maxLen,
                                    EnumValues = enumVals,
                                    IsSequenceOfPrimitive = isSeqPrim,
                                    IsSequenceOfString = isSeqStr,
                                    IsKey = isKey
                                };

                                fields.Add(newField);
                            }
                            var schema = new TypeSchema { Name = name, Fields = fields };
                            _cache[name] = schema;
                        }
                    }
                    catch { /* ignore file parse errors */ }
                }
            }
            catch { }
        }

        public TypeSchema? GetSchema(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            // try exact
            if (_cache.TryGetValue(typeName, out var s)) return s;
            // try strip module prefix 'Module::C_Name' -> 'C_Name'
            var idx = typeName.IndexOf("::");
            var shortName = idx >= 0 ? typeName.Substring(idx + 2) : typeName;
            _cache.TryGetValue(shortName, out s);
            return s;
        }

        /// <summary>
        /// 캐시된 모든 타입 이름을 반환합니다. UI에서 목록을 채우기 위해 사용됩니다.
        /// </summary>
        public System.Collections.Generic.IEnumerable<string> GetTypeNames()
        {
            // Return keys in stable order
            return _cache.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        public System.Collections.Generic.IEnumerable<string>? GetEnumValues(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            if (_enums.TryGetValue(typeName, out var vals)) return vals;
            var idx = typeName.IndexOf("::");
            var shortName = idx >= 0 ? typeName.Substring(idx + 2) : typeName;
            _enums.TryGetValue(shortName, out vals);
            return vals;
        }

        private void ResolveTypedef(string typeName, out string? resolved, out TypedefInfo? tdInfo)
        {
            resolved = null;
            tdInfo = null;
            if (string.IsNullOrWhiteSpace(typeName)) return;
            // try exact
            if (_typedefs.TryGetValue(typeName, out var info))
            {
                tdInfo = info;
                resolved = info.Underlying;
                // if underlying is another typedef name, try one level more
                if (!string.IsNullOrWhiteSpace(resolved) && _typedefs.TryGetValue(resolved, out var info2))
                {
                    tdInfo = info2;
                    resolved = info2.Underlying;
                }
                return;
            }
            // try short name (strip module)
            var idx = typeName.IndexOf("::");
            var shortName = idx >= 0 ? typeName.Substring(idx + 2) : typeName;
            if (_typedefs.TryGetValue(shortName, out var info3))
            {
                tdInfo = info3;
                resolved = info3.Underlying;
                if (!string.IsNullOrWhiteSpace(resolved) && _typedefs.TryGetValue(resolved, out var info4))
                {
                    tdInfo = info4;
                    resolved = info4.Underlying;
                }
            }
        }

        private string InferKindFromType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "string";
            var t = type.ToLowerInvariant();
            if (t.Contains("int")) return "int";
            if (t.Contains("double") || t.Contains("float")) return "double";
            if (t == "bool" || t == "boolean") return "bool";
            // enum detection: if the type looks like a named enum (ends with _type) or contains 'enum'
            if (t.Contains("enum") || t.EndsWith("_type")) return "enum";
            // struct detection: if the type references a known struct in cache or contains 't_' pattern
            if (_cache.ContainsKey(type) || t.StartsWith("c_") || t.Contains("struct") || t.Contains("t_") ) return "struct";
            if (t.EndsWith("[]")) return "sequence";
            return "string";
        }
    }
}
