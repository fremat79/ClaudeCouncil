using System.IO;

namespace ClaudeCouncilManager.Services;

/// <summary>Reads the markdown templates embedded from the Templates folder (see the .csproj).</summary>
public static class Templates
{
    public static string Load(string fileName)
    {
        using var stream = typeof(Templates).Assembly.GetManifestResourceStream("Templates." + fileName)
            ?? throw new FileNotFoundException("Missing template: " + fileName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
