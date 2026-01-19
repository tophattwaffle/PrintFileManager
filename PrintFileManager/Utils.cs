using System.Reflection;

namespace PrintFileManager;

public class Utils
{
    private static readonly object _lock = new();
    public static void Log(string msg)
    {
        string line = $"{DateTime.Now} | {msg}\n";
        lock (_lock)
        {
            Console.Write(line);
            if(!string.IsNullOrEmpty(Program.LogPath) && Program.LogToFile)
                File.AppendAllText(Program.LogPath, line);
        }
    }
    
    public static string GetBuildDate()
    {
        var attribute = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
    
        if (attribute?.InformationalVersion != null)
        {
            var version = attribute.InformationalVersion;
            // Extracts the part after "build"
            int index = version.LastIndexOf("build");
            if (index > 0)
            {
                return version.Substring(index + 5).Trim();
            }
        }
        return "Unknown";
    }
}