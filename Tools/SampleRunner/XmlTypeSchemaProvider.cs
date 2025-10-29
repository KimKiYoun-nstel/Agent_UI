using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Tools.SampleRunner.Services
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
        private HashSet<string> _allStructNames = new(StringComparer.Ordinal);

        public XmlTypeSchemaProvider(string configRoot)
        {
            // 두 단계 처리: 먼저 모든 파일을 읽어 typedef/enum/struct 이름을 수집한 다음,
            // 두 번째 단계에서 struct의 필드를 파싱하여 _cache를 채웁니다.
            try
            {
                var gen = Path.Combine(configRoot ?? string.Empty, "generated");
                if (!Directory.Exists(gen)) return;
                var files = Directory.GetFiles(gen, "*.xml");

                var docs = new List<XDocument>();
                foreach (var f in files)
                {
                    try { docs.Add(XDocument.Load(f)); } catch { }
                }

                // 1) typedefs, enums, struct 이름 수집
                var allStructNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var doc in docs)
                {
                    try
                    {
                        var tdefs = doc.Descendants().Where(e => e.Name.LocalName == "typedef");
                        var enums = doc.Descendants().Where(e => e.Name.LocalName == "enum");
                        var structs = doc.Descendants().Where(e => e.Name.LocalName == "struct");

                        foreach (var td in tdefs)
                        {
                            var tname = td.Attribute("name")?.Value;
                            var typeAttr = td.Attribute("type")?.Value ?? td.Element("type")?.Value;
                            var nonBasic = td.Attribute("nonBasicTypeName")?.Value ?? td.Element("nonBasicTypeName")?.Value;
                            var seqMax = td.Attribute("sequenceMaxLength")?.Value ?? td.Element("sequenceMaxLength")?.Value;
                            var strMax = td.Attribute("stringMaxLength")?.Value ?? td.Element("stringMaxLength")?.Value;
                            if (string.IsNullOrWhiteSpace(tname)) continue;
                            var underlying = !string.IsNullOrWhiteSpace(nonBasic) ? nonBasic : typeAttr ?? "";
                            int? smax = null; if (int.TryParse(seqMax, out var sm)) smax = sm;
                            int? strm = null; if (int.TryParse(strMax, out var stm)) strm = stm;
                            _typedefs[tname] = new TypedefInfo { Underlying = underlying, SequenceMax = smax, StringMax = strm };
                            var idx = tname.IndexOf("::");
                            if (idx >= 0)
                            {
                                var shortn = tname.Substring(idx + 2);
                                if (!_typedefs.ContainsKey(shortn)) _typedefs[shortn] = _typedefs[tname];
                            }
                        }

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
                            if (!string.IsNullOrWhiteSpace(name)) allStructNames.Add(name);
                        }
                    }
                    catch { }
                }

                // store allStructNames for later use in kind inference
                _allStructNames = allStructNames;

                // 2) 두 번째 패스: 각 struct의 멤버를 파싱하여 _cache 채우기
                foreach (var doc in docs)
                {
                    try
                    {
                        var structs = doc.Descendants().Where(e => e.Name.LocalName == "struct");
                        foreach (var s in structs)
                        {
                            var name = s.Attribute("name")?.Value;
                            if (string.IsNullOrWhiteSpace(name)) continue;
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

                                var effectiveType = !string.IsNullOrWhiteSpace(nonBasic) ? nonBasic : ftype;

                                string? resolvedType = null; TypedefInfo? tdInfo = null;
                                if (!string.IsNullOrWhiteSpace(effectiveType)) ResolveTypedef(effectiveType, out resolvedType, out tdInfo);
                                var finalType = resolvedType ?? effectiveType;

                                List<string>? enumVals = null;
                                if (m.Elements().Any(x => x.Name.LocalName == "enum" || x.Name.LocalName == "enumerator"))
                                {
                                    enumVals = m.Elements().Where(x => x.Name.LocalName == "enum" || x.Name.LocalName == "enumerator").Select(x => x.Value).Where(v => v != null).ToList();
                                }

                                if (string.IsNullOrWhiteSpace(fname)) continue;

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
                                    var isSequence = tdInfo?.SequenceMax != null || !string.IsNullOrWhiteSpace(seqMax) || (finalType?.EndsWith("[]") ?? false) || (finalType?.StartsWith("sequence<") ?? false) || finalType?.Contains("sequence") == true;
                                    if (isSequence)
                                    {
                                        kind = "sequence";
                                        typeVal = finalType ?? effectiveType;
                                        var elem = finalType!;
                                        if (elem.EndsWith("[]")) elem = elem.Substring(0, elem.Length - 2);
                                        var sidx = elem.IndexOf("<");
                                        var eidx = elem.IndexOf(">");
                                        if (sidx >= 0 && eidx > sidx) elem = elem.Substring(sidx + 1, eidx - sidx - 1);
                                        seqElemType = elem;
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
            // 개선: typedef 체인을 안전하게 최대 깊이까지 추적하도록 변경합니다.
            resolved = null;
            tdInfo = null;
            if (string.IsNullOrWhiteSpace(typeName)) return;

            // candidate를 점진적으로 업데이트하면서 typedef 맵에서 찾습니다.
            var candidate = typeName;
            TypedefInfo? lastFound = null;
            for (int depth = 0; depth < 6; depth++)
            {
                if (string.IsNullOrWhiteSpace(candidate)) break;

                if (_typedefs.TryGetValue(candidate, out var info))
                {
                    lastFound = info;
                    // 다음으로 추적할 후보는 underlying
                    if (!string.IsNullOrWhiteSpace(info.Underlying) && info.Underlying != candidate)
                    {
                        candidate = info.Underlying;
                        continue;
                    }
                    // underlying가 없거나 동일하면 중단
                    candidate = info.Underlying;
                    break;
                }

                // try short name (strip module)
                var idx = candidate.IndexOf("::");
                if (idx >= 0)
                {
                    var shortName = candidate.Substring(idx + 2);
                    if (_typedefs.TryGetValue(shortName, out var info2))
                    {
                        lastFound = info2;
                        if (!string.IsNullOrWhiteSpace(info2.Underlying) && info2.Underlying != candidate)
                        {
                            candidate = info2.Underlying;
                            continue;
                        }
                        candidate = info2.Underlying;
                        break;
                    }
                }

                // 못찾으면 중단
                break;
            }

            if (lastFound != null)
            {
                tdInfo = lastFound;
                resolved = lastFound.Underlying;
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
            // sequence detection
            if (t.EndsWith("[]")) return "sequence";

            // struct detection: check cache for either full name or short (module-stripped) name,
            // and also keep other heuristic checks for c_ prefixes or explicit 'struct' tokens.
            var shortName = type;
            var idx = type.IndexOf("::");
            if (idx >= 0) shortName = type.Substring(idx + 2);
            if (_cache.ContainsKey(type) || _cache.ContainsKey(shortName) || _allStructNames.Contains(shortName) || t.StartsWith("c_") || t.Contains("struct")) return "struct";

            return "string";
        }
    }
}
