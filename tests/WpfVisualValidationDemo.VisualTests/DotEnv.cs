using System.IO;
namespace WpfVisualValidationDemo.VisualTests;

/// Loads KEY=VALUE pairs from the nearest .env up the tree into the process environment (existing vars win).
internal static class DotEnv
{
    public static void Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var file = Path.Combine(dir.FullName, ".env");
            if (File.Exists(file))
            {
                foreach (var raw in File.ReadAllLines(file))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line[..eq].Trim();
                    var value = line[(eq + 1)..].Trim().Trim('"');
                    if (value.Length == 0) continue;
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                        Environment.SetEnvironmentVariable(key, value);
                }
                return;
            }
            dir = dir.Parent;
        }
    }
}
