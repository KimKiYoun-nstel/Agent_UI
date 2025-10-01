using System;
using System.IO;
using System.Linq;

namespace Agent.UI.Wpf.Services
{
    public static class ConfigLocator
    {
        public static string Resolve(string[] args)
        {
            var cli = args.FirstOrDefault(a => a.StartsWith("--config=", StringComparison.OrdinalIgnoreCase));
            if (cli != null) return cli.Substring("--config=".Length).Trim('"');

            var env = Environment.GetEnvironmentVariable("AGENT_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(env)) return env;

            return Path.Combine(AppContext.BaseDirectory, "config");
        }
    }
}
