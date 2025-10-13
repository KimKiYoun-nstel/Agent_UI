using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Agent.UI.Wpf.Services
{
    /// <summary>
    /// config 디렉터리의 XML을 파싱하여 토픽 타입명과 QoS 프로필명을 로드
    /// </summary>
    public static class ConfigService
    {
        /// <summary>
        /// 서비스 내부 로그 콜백 (기본값: Debug.WriteLine). 앱에서 할당하면 UI로 로그를 전달할 수 있습니다.
        /// </summary>
        public static Action<string>? LogAction;

        private static void Log(string msg)
        {
            if (LogAction != null)
                LogAction(msg);
            else
                Debug.WriteLine(msg);
        }
        /// <summary>
        /// generated 폴더의 XML에서 타입 이름 목록 추출
        /// </summary>
        public static List<string> LoadTypeNames(string configRoot)
        {
            var results = new HashSet<string>(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(configRoot)) return new List<string>();

            var generatedDir = Path.Combine(configRoot, "generated");
            if (!Directory.Exists(generatedDir))
            {
                Log($"ConfigService: generated directory not found: {generatedDir}");
                return new List<string>();
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(generatedDir, "*.xml");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ConfigService: failed to enumerate generated files: {ex.Message}");
                return new List<string>();
            }

            foreach (var file in files)
            {
                try
                {
                    var doc = XDocument.Load(file);
                    var structElems = doc.Descendants().Where(e => e.Name.LocalName == "struct");
                    foreach (var e in structElems)
                    {
                        var nameAttr = e.Attribute("name")?.Value;
                        if (string.IsNullOrEmpty(nameAttr) || !nameAttr.StartsWith("C_", StringComparison.Ordinal)) continue;
                        // find enclosing module name if present
                        var module = e.Ancestors().FirstOrDefault(a => a.Name.LocalName == "module");
                        var moduleName = module?.Attribute("name")?.Value;
                        if (!string.IsNullOrEmpty(moduleName))
                        {
                            results.Add($"{moduleName}::{nameAttr}");
                        }
                        else
                        {
                            results.Add(nameAttr);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"ConfigService: failed to parse '{file}': {ex.Message}");
                    // continue with other files
                }
            }

            var list = results.ToList();
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>
        /// qos 폴더의 XML에서 QoS 프로필 이름 목록 추출
        /// </summary>
        public static List<string> LoadQosProfiles(string configRoot)
        {
            var results = new HashSet<string>(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(configRoot)) return new List<string>();

            var qosDir = Path.Combine(configRoot, "qos");
            if (!Directory.Exists(qosDir))
            {
                Log($"ConfigService: qos directory not found: {qosDir}");
                return new List<string>();
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(qosDir, "*.xml");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ConfigService: failed to enumerate qos files: {ex.Message}");
                return new List<string>();
            }

            string? defaultProfile = null;
            foreach (var file in files)
            {
                try
                {
                    var doc = XDocument.Load(file);

                    var libElems = doc.Descendants().Where(e => e.Name.LocalName == "qos_library");
                    foreach (var lib in libElems)
                    {
                        var libName = lib.Attribute("name")?.Value?.Trim() ?? string.Empty;
                        var profileElems = lib.Descendants().Where(e => e.Name.LocalName == "qos_profile");
                        foreach (var p in profileElems)
                        {
                            var profileName = p.Attribute("name")?.Value?.Trim();
                            if (string.IsNullOrEmpty(profileName)) continue;

                            var fullName = string.IsNullOrEmpty(libName) ? profileName : $"{libName}::{profileName}";
                            results.Add(fullName);

                            // is_default_qos 처리: 첫 발견된 default는 우선 저장
                            var isDefault = p.Attribute("is_default_qos")?.Value;
                            if (defaultProfile == null && !string.IsNullOrEmpty(isDefault) && bool.TryParse(isDefault, out var b) && b)
                            {
                                defaultProfile = fullName;
                                Log($"ConfigService: found default QoS '{fullName}' in file '{file}'");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"ConfigService: failed to parse QoS file '{file}': {ex.Message}");
                }
            }

            var list = results.ToList();
            list.Sort(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(defaultProfile) && list.Remove(defaultProfile))
            {
                list.Insert(0, defaultProfile);
            }

            return list;
        }
    }
}
