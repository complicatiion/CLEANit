using CLEANit.Core.Storage;

namespace CLEANit.Platform.Windows;

/// <summary>Last program list with time stamp (data\cache\programs.json): shown at once, refreshed on request.</summary>
public sealed class ProgramCache
{
    public DateTime SavedUtc { get; set; }

    public List<InstalledProgram> Programs { get; set; } = [];

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "cache", "programs.json");

    public static ProgramCache? Load()
    {
        try
        {
            return JsonFile.Read<ProgramCache>(FilePath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Save(IEnumerable<InstalledProgram> programs)
    {
        try
        {
            JsonFile.Write(FilePath, new ProgramCache { SavedUtc = DateTime.UtcNow, Programs = programs.ToList() });
        }
        catch (Exception)
        {
            // Cache only.
        }
    }

    public static void Invalidate()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception)
        {
            // Not present.
        }
    }
}
