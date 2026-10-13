namespace MS.Microservice.Lab.Application.Tools;

internal static class ToolDemoDefaults
{
    internal static string? Value(IConfiguration configuration, string name)
    {
        var value = configuration["ToolDemos:" + name];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static string? FontFile(IConfiguration configuration)
    {
        if (Value(configuration, "FontFilePath") is { } configured)
        {
            if (!File.Exists(configured)) throw new ToolDependencyException("ToolDemos:FontFilePath");
            return configured;
        }

        // The platform default typeface may lack Chinese glyphs on Windows.
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrEmpty(directory))
            foreach (var name in new[] { "msyh.ttc", "simhei.ttf", "simsun.ttc" })
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    internal static string LibreOfficeExecutable(IConfiguration configuration)
    {
        const string key = "ToolDemos:LibreOfficeExecutablePath";
        if (Value(configuration, "LibreOfficeExecutablePath") is { } configured)
        {
            if (!Path.IsPathFullyQualified(configured) || !File.Exists(configured)) throw new ToolDependencyException(key);
            return configured;
        }

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => directory.Trim().Trim('"')).Where(Path.IsPathFullyQualified);
        var candidates = directories.Select(directory => Path.Combine(directory, "soffice.com"))
            .Concat(new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .Select(Environment.GetFolderPath).Where(directory => !string.IsNullOrEmpty(directory))
                .Select(directory => Path.Combine(directory, "LibreOffice", "program", "soffice.com")));
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new ToolDependencyException(key + "（请安装 LibreOffice，或填写 program/soffice.com 的绝对路径）");
    }
}
