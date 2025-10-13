using System;
using System.Windows;
using Agent.UI.Wpf.Services;
using Agent.UI.Wpf.ViewModels;

namespace Agent.UI.Wpf
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var args = Environment.GetCommandLineArgs();
            var cfgDir = ConfigLocator.Resolve(args);
            var autoArg = args.Length > 1 ? args[1] : null;
            var vm = new MainViewModel(cfgDir, new ClockService(), autoArg);

            var win = new Views.MainWindow { DataContext = vm };
            win.Show();

            /// -----------------------------------------------------------------
            /// \brief 개발용 샘플 검증기 (실행 인자: --verify-samples)
            ///
            /// \details
            /// 이 루틴은 개발·디버그 목적으로 로컬의 기대 샘플(JSON)과
            /// `SampleJsonBuilder`의 출력을 비교하여 회귀를 빠르게 검출하기 위한
            /// 보조 기능입니다. 기본적으로 비활성화되어 있으며, 활성화하려면
            /// 애플리케이션을 `--verify-samples` 플래그로 실행합니다.
            ///
            /// \note
            /// - 비파괴적이며(파일/네트워크/앱 상태를 변경하지 않음) 백그라운드
            ///   태스크에서 실행되어 UI 스레드를 차단하지 않습니다.
            /// - 기대 샘플 파일 형식과 비교 기준은 `xml_parsing_rule.md`에 문서화되어
            ///   있습니다.
            /// - 이 주석은 프로젝트의 Doxygen 스타일(한글) 규칙을 따릅니다.
            /// -----------------------------------------------------------------
            if (args != null && args.Length > 1 && string.Equals(args[1], "--verify-samples", StringComparison.OrdinalIgnoreCase))
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        // brief delay to let UI initialize
                        await System.Threading.Tasks.Task.Delay(200);
                        // find project root
                        string FindProjectRoot(string start)
                        {
                            var dir = new System.IO.DirectoryInfo(start);
                            while (dir != null)
                            {
                                var csproj = System.IO.Path.Combine(dir.FullName, "Agent.UI.Wpf.csproj");
                                if (System.IO.File.Exists(csproj)) return dir.FullName;
                                dir = dir.Parent;
                            }
                            return start;
                        }

                        var projectDir = FindProjectRoot(AppContext.BaseDirectory);
                        var samplePath = System.IO.Path.Combine(projectDir, "XML_파서_JSON_샘플.json");
                        Console.WriteLine($"[Verifier] projectDir={projectDir}");
                        Console.WriteLine($"[Verifier] sample file: {samplePath}");

                        var provider = new Services.XmlTypeSchemaProvider(cfgDir);
                        var builder = new Services.SampleJsonBuilder(provider);

                        // load expected samples from file
                        var expected = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);
                        if (System.IO.File.Exists(samplePath))
                        {
                            var lines = System.IO.File.ReadAllLines(samplePath);
                            string? curType = null;
                            var sb = new System.Text.StringBuilder();
                            bool inJson = false;
                            int braceDepth = 0;
                            foreach (var raw in lines)
                            {
                                var line = raw.TrimEnd('\r');
                                if (!inJson && string.IsNullOrWhiteSpace(line)) continue;
                                if (!inJson && line.StartsWith("C_") )
                                {
                                    // commit previous
                                    if (curType != null && sb.Length > 0)
                                    {
                                        expected[curType] = sb.ToString().Trim();
                                        sb.Clear();
                                    }
                                    curType = line.Trim();
                                    inJson = false;
                                    braceDepth = 0;
                                    continue;
                                }
                                // look for JSON start
                                if (curType != null)
                                {
                                    var idx = line.IndexOf('{');
                                    if (!inJson && idx >= 0)
                                    {
                                        inJson = true;
                                    }
                                    if (inJson)
                                    {
                                        sb.AppendLine(line);
                                        foreach (var ch in line)
                                        {
                                            if (ch == '{') braceDepth++;
                                            else if (ch == '}') braceDepth--;
                                        }
                                        if (braceDepth == 0 && sb.Length > 0)
                                        {
                                            expected[curType] = sb.ToString().Trim();
                                            sb.Clear();
                                            inJson = false;
                                            curType = null;
                                        }
                                    }
                                }
                            }
                            // final commit
                            if (curType != null && sb.Length > 0) expected[curType] = sb.ToString().Trim();
                        }
                        else
                        {
                            Console.WriteLine("[Verifier] Expected sample file not found.");
                        }

                        string[] types = new[] { "C_Crew_Role_In_Mission_State", "C_Actual_Alarm", "C_Tone_Specification" };
                        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

                        bool JsonEquals(System.Text.Json.JsonElement a, System.Text.Json.JsonElement b)
                        {
                            if (a.ValueKind != b.ValueKind) return false;
                            switch (a.ValueKind)
                            {
                                case System.Text.Json.JsonValueKind.Object:
                                    {
                                        var ap = a.EnumerateObject();
                                        var bp = b.EnumerateObject();
                                        var aDict = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>();
                                        foreach (var p in ap) aDict[p.Name] = p.Value;
                                        foreach (var p in bp)
                                        {
                                            if (!aDict.TryGetValue(p.Name, out var av)) return false;
                                            if (!JsonEquals(av, p.Value)) return false;
                                        }
                                        return aDict.Count == b.EnumerateObject().Count();
                                    }
                                case System.Text.Json.JsonValueKind.Array:
                                    {
                                        var aa = a.EnumerateArray().ToArray();
                                        var bb = b.EnumerateArray().ToArray();
                                        if (aa.Length != bb.Length) return false;
                                        for (int i = 0; i < aa.Length; i++) if (!JsonEquals(aa[i], bb[i])) return false;
                                        return true;
                                    }
                                case System.Text.Json.JsonValueKind.String:
                                    return a.GetString() == b.GetString();
                                case System.Text.Json.JsonValueKind.Number:
                                    return a.GetDecimal() == b.GetDecimal();
                                case System.Text.Json.JsonValueKind.True:
                                case System.Text.Json.JsonValueKind.False:
                                    return a.GetBoolean() == b.GetBoolean();
                                case System.Text.Json.JsonValueKind.Null:
                                    return true;
                                default:
                                    return a.ToString() == b.ToString();
                            }
                        }

                        foreach (var t in types)
                        {
                            Console.WriteLine($"\n[Verifier] === Type: {t} ===");
                            var schema = provider.GetSchema(t);
                            if (schema == null)
                            {
                                Console.WriteLine($"[Verifier] Schema not found for {t}.");
                                continue;
                            }
                            Console.WriteLine($"[Verifier] Schema name: {schema.Name}");
                            foreach (var f in schema.Fields)
                            {
                                Console.WriteLine($"[Verifier] - {f.Name}: kind={f.Kind}, type={f.Type}, nested={f.NestedType}, seqElem={f.SequenceElementType}, upper={f.UpperBound}, maxLen={f.MaxLen}, isSeqPrim={f.IsSequenceOfPrimitive}, isSeqStr={f.IsSequenceOfString}, isKey={f.IsKey}");
                            }

                            // build sample
                            try
                            {
                                var sample = builder.BuildSample(t);
                                var gotJson = System.Text.Json.JsonSerializer.Serialize(sample, options);
                                Console.WriteLine("[Verifier] Generated JSON:");
                                Console.WriteLine(gotJson);

                                if (expected.TryGetValue(t, out var expJsonRaw))
                                {
                                    try
                                    {
                                        using var docGot = System.Text.Json.JsonDocument.Parse(gotJson);
                                        using var docExp = System.Text.Json.JsonDocument.Parse(expJsonRaw);
                                        var equal = JsonEquals(docGot.RootElement, docExp.RootElement);
                                        Console.WriteLine($"[Verifier] Comparison result: {(equal ? "PASS" : "FAIL")}");
                                        if (!equal)
                                        {
                                            Console.WriteLine("[Verifier] Expected JSON:");
                                            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonDocument.Parse(expJsonRaw).RootElement, options));
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[Verifier] Failed to parse expected JSON for {t}: {ex.Message}");
                                        Console.WriteLine("[Verifier] Raw expected:");
                                        Console.WriteLine(expJsonRaw);
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"[Verifier] No expected sample available for {t}.");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Verifier] BuildSample failed: {ex.Message}");
                            }
                        }

                        Console.WriteLine("[Verifier] Done.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Verifier] Unexpected error: {ex.Message}");
                    }
                });
            }

            // If caller requested auto-connect, trigger it after window shows
            if (string.Equals(autoArg, "--autoconnect", StringComparison.OrdinalIgnoreCase))
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    // brief delay to let UI initialize
                    await System.Threading.Tasks.Task.Delay(200);
                    try
                    {
                        // invoke on UI thread
                        win.Dispatcher.Invoke(() =>
                        {
                            if (vm.ConnectCommand.CanExecute(null)) vm.ConnectCommand.Execute(null);
                        });
                    }
                    catch { }
                });
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            if (Current.MainWindow?.DataContext is IAsyncDisposable d)
            {
                try { await d.DisposeAsync(); } catch { }
            }
        }
    }
}
