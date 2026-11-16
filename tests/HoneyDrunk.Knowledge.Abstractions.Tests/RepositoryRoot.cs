namespace HoneyDrunk.Knowledge.Abstractions.Tests;

/// <summary>Finds the repository root from the test output directory.</summary>
internal static class RepositoryRoot
{
    private const string SolutionFile = "HoneyDrunk.Knowledge.slnx";

    /// <summary>Returns the directory that holds the solution file.</summary>
    /// <returns>The repository root.</returns>
    public static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(SolutionFile + " was not found above the test output directory.");
    }
}
