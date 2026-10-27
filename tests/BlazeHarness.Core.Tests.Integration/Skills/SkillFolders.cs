using BlazeHarness.Core.Skills;

namespace BlazeHarness.Core.Tests.Integration.Skills;

/// <summary>
/// A temporary folder to build skill folders in, removed when disposed.
/// </summary>
internal sealed class SkillFolders : IDisposable
{
    public SkillFolders()
    {
        Root = Path.Combine(Path.GetTempPath(), "bh-skills-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string AddRaw(string container, string folderName, string skillText)
    {
        var folder = Path.Combine(Root, container, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, SkillInfo.FileName), skillText);
        return folder;
    }

    public string Add(string container, string name, string description, string body = "Do the thing.")
    {
        return AddRaw(container, name, $"---\nname: {name}\ndescription: {description}\n---\n{body}\n");
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }
}
