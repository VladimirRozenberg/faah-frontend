using System;
using System.IO;
using Avalonia;

namespace FAAH_Frontend;

internal static class Program
{
    [System.STAThread]
    public static void Main(string[] args)
    {
        LoadApiUrlFromDotEnv();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void LoadApiUrlFromDotEnv()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FAAH_API_URL"))) return;

        string? path = FindDotEnvPath();
        if (path is null) return;

        foreach (string line in File.ReadLines(path))
        {
            string entry = line.Trim();
            if (entry.Length == 0 || entry.StartsWith('#')) continue;

            int separator = entry.IndexOf('=');
            if (separator <= 0 || !string.Equals(entry[..separator].Trim(), "FAAH_API_URL", StringComparison.Ordinal))
                continue;

            string value = entry[(separator + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            if (!string.IsNullOrWhiteSpace(value))
                Environment.SetEnvironmentVariable("FAAH_API_URL", value);
            return;
        }
    }

    private static string? FindDotEnvPath()
    {
        foreach (string startPath in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            DirectoryInfo? directory = new(startPath);
            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, ".env");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
        }

        return null;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
