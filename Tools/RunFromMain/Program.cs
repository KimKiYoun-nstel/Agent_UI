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
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var assemblyPath = Path.GetFullPath(Path.Combine(repoRoot, "bin", "Debug", "net8.0-windows", "Agent.UI.Wpf.dll"));
            if (!File.Exists(assemblyPath))
            {
                Console.Error.WriteLine("Assembly not found: " + assemblyPath);
                return 2;
            }

            // Use the repository config folder so we operate on the real config/generated files
            var configRoot = Path.GetFullPath(Path.Combine(repoRoot, "config"));

            var asm = Assembly.LoadFrom(assemblyPath);

            var providerType = asm.GetType("Agent.UI.Wpf.Services.XmlTypeSchemaProvider");
            var builderType = asm.GetType("Agent.UI.Wpf.Services.SampleJsonBuilder");

            if (providerType == null || builderType == null)
            {
                Console.Error.WriteLine("Required types not found in assembly.");
                return 3;
            }

            // create provider
            var provider = Activator.CreateInstance(providerType, new object?[] { configRoot });
            if (provider == null) { Console.Error.WriteLine("Failed to create provider"); return 4; }

            // create builder(provider)
            var builder = Activator.CreateInstance(builderType, new object?[] { provider });
            if (builder == null) { Console.Error.WriteLine("Failed to create builder"); return 5; }

            // get methods
            var getTypeNames = providerType.GetMethod("GetTypeNames");
            var getSchema = providerType.GetMethod("GetSchema");
            var buildSample = builderType.GetMethod("BuildSample");

            if (getTypeNames == null || buildSample == null)
            {
                Console.Error.WriteLine("Missing expected methods");
                return 6;
            }

            var names = ((System.Collections.IEnumerable)getTypeNames.Invoke(provider, null)!).Cast<object?>().Select(x => x?.ToString() ?? "").ToList();
            Console.WriteLine("Loaded types from main assembly provider (count=" + names.Count + ")");

            var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

            // Find all XML files under config/generated and for each collect struct names starting with C_
            var generatedDir = Path.Combine(configRoot, "generated");
            if (!Directory.Exists(generatedDir))
            {
                Console.Error.WriteLine("config/generated not found at: " + generatedDir);
                return 7;
            }

            var xmlFiles = Directory.GetFiles(generatedDir, "*.xml");
            foreach (var xf in xmlFiles)
            {
                var doc = System.Xml.Linq.XDocument.Load(xf);
                var structs = doc.Descendants("struct")
                    .Where(e => (string?)e.Attribute("name") != null && ((string)e.Attribute("name")!).StartsWith("C_"))
                    .Select(e => ((string)e.Attribute("name")!))
                    .Distinct()
                    .ToList();

                if (structs.Count == 0) continue; // skip files without C_ structs

                Console.WriteLine($"Processing file {Path.GetFileName(xf)} with {structs.Count} C_ types");

                var outMap = new System.Collections.Generic.Dictionary<string, object?>();
                foreach (var t in structs)
                {
                    try
                    {
                        var sample = buildSample.Invoke(builder, new object?[] { t });
                        outMap[t] = sample;
                    }
                    catch (Exception ex)
                    {
                        outMap[t] = new { _error = ex.Message };
                    }
                }

                var outJson = JsonSerializer.Serialize(outMap, options);
                var outName = "xml_to_json_sample_" + Path.GetFileNameWithoutExtension(xf) + ".json";
                var outPath = Path.Combine(repoRoot, outName);
                File.WriteAllText(outPath, outJson);
                Console.WriteLine("Wrote: " + outPath);
            }

            // Diagnostic: inspect specific field MaxLen for C_Alarm_Category_Specification
            var diagType = "C_Alarm_Category_Specification";
            var schemaDiag = getSchema?.Invoke(provider, new object?[] { diagType });
            if (schemaDiag != null)
            {
                Console.WriteLine($"\nDiagnostic for {diagType} fields:");
                var fieldsProp = schemaDiag.GetType().GetProperty("Fields");
                if (fieldsProp != null)
                {
                    var fields = (System.Collections.IEnumerable)fieldsProp.GetValue(schemaDiag)!;
                    foreach (var f in fields)
                    {
                        var fname = f.GetType().GetProperty("Name")?.GetValue(f)?.ToString();
                        var fkind = f.GetType().GetProperty("Kind")?.GetValue(f)?.ToString();
                        var ftype = f.GetType().GetProperty("Type")?.GetValue(f)?.ToString();
                        var fmax = f.GetType().GetProperty("MaxLen")?.GetValue(f);
                        var fub = f.GetType().GetProperty("UpperBound")?.GetValue(f);
                        Console.WriteLine($"  {fname}: Kind={fkind}, Type={ftype}, MaxLen={fmax}, UpperBound={fub}");
                    }
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 99;
        }
    }
}
