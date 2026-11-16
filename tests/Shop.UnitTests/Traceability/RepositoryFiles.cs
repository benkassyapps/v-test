namespace Shop.UnitTests.Traceability;

/// <summary>Accès aux fichiers du dépôt (specs et sources de test) depuis le dossier d'exécution des tests.</summary>
internal static class RepositoryFiles
{
    public static DirectoryInfo Root { get; } = FindRoot();

    public static IEnumerable<FileInfo> RequirementFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root.FullName, "specs"), "requirements.md", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path));

    public static IEnumerable<FileInfo> FeatureFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root.FullName, "specs"), "*.feature", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path));

    /// <summary>
    /// Sources C# des projets de test, hors bin/obj et hors dossier Traceability
    /// (ses exemples contiennent volontairement des références fictives).
    /// </summary>
    public static IEnumerable<FileInfo> TestSourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root.FullName, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnder(path, "bin") && !IsUnder(path, "obj") && !IsUnder(path, "Traceability"))
            .Select(path => new FileInfo(path));

    public static string RelativePath(FileInfo file) =>
        Path.GetRelativePath(Root.FullName, file.FullName).Replace('\\', '/');

    private static bool IsUnder(string path, string folderName) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains(folderName, StringComparer.OrdinalIgnoreCase);

    private static DirectoryInfo FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Shop.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Racine du dépôt introuvable (Shop.slnx absent).");
    }
}
