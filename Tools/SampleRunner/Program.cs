using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            // repo root: assume repository folder is 5 levels up from build output
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var configRoot = Path.GetFullPath(Path.Combine(repoRoot, "bin", "Debug", "net8.0-windows", "config"));

            // Instantiate local provider and builder (copied into this Tools project)
            var provider = new Tools.SampleRunner.Services.XmlTypeSchemaProvider(configRoot);
            var builder = new Tools.SampleRunner.Services.SampleJsonBuilder(provider);

            Console.WriteLine("Loaded types from provider:");
            try
            {
                foreach (var n in provider.GetTypeNames().Take(200)) Console.WriteLine("  " + n);
                Console.WriteLine();
                Console.WriteLine("Schema lookup checks:");
                Console.WriteLine("  GetSchema(\"T_IdentifierType\") != null -> " + (provider.GetSchema("T_IdentifierType") != null));
                Console.WriteLine("  GetSchema(\"P_LDM_Common::T_IdentifierType\") != null -> " + (provider.GetSchema("P_LDM_Common::T_IdentifierType") != null));
            }
            catch (Exception ex)
            {
                Console.WriteLine("  (failed to enumerate types) " + ex.Message);
            }

            var typesToCheck = new[] { "C_Actual_Alarm", "C_Crew_Role_In_Mission_State", "C_Tone_Specification", "C_Alarm_Category_Specification" };

            var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

            foreach (var t in typesToCheck)
            {
                Console.WriteLine($"--- Schema fields for {t} ---");
                var schema = provider.GetSchema(t);
                if (schema != null)
                {
                    foreach (var f in schema.Fields)
                    {
                        var inferred = BasicInferKind(f.Type);
                        Console.WriteLine($"  {f.Name}: Kind={f.Kind}, Type={f.Type}, InferredLocalKind={inferred}, NestedType={f.NestedType}, SeqElem={f.SequenceElementType}");
                    }
                }
                Console.WriteLine($"--- Generated sample for {t} ---");
                var sample = builder.BuildSample(t);
                var json = JsonSerializer.Serialize(sample, options);
                Console.WriteLine(json);
                Console.WriteLine();
            }

            // Also print attached expected file if present
            var expectedPath = Path.GetFullPath(Path.Combine(repoRoot, "XML_파서_JSON_샘플.json"));
            if (File.Exists(expectedPath))
            {
                Console.WriteLine("--- Expected sample (attached XML_파서_JSON_샘플.json) ---");
                var expected = File.ReadAllText(expectedPath);
                Console.WriteLine(expected);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 99;
        }
    }

    static string BasicInferKind(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "string";
        var t = type.ToLowerInvariant();
        if (t.Contains("int")) return "int";
        if (t.Contains("double") || t.Contains("float")) return "double";
        if (t == "bool" || t == "boolean") return "bool";
        if (t.Contains("enum") || t.EndsWith("_type")) return "enum";
        if (t.EndsWith("[]")) return "sequence";
        var shortName = type;
        var idx = type.IndexOf("::");
        if (idx >= 0) shortName = type.Substring(idx + 2);
        // emulate provider's cache check via GetSchema
        try
        {
            // rudimentary check: if shortName looks like T_ or C_ treat as struct candidate
            if (shortName.StartsWith("c_") || shortName.StartsWith("t_") || shortName.StartsWith("C_") || shortName.StartsWith("T_")) return "struct";
        }
        catch { }
        return "string";
    }
}
