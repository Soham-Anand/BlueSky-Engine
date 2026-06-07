namespace BlueSky.Editor.Services;

/// <summary>
/// Editor-facing project lifecycle state (launcher inputs + open project).
/// Wraps ProjectManager/ProjectConfig so Program stops owning raw fields.
/// </summary>
public sealed class ProjectService
{
    public string ProjectPathInput { get; set; } = "";
    public string ProjectNameInput { get; set; } = "MyGame";
    public string OpenProjectPathInput { get; set; } = "";

    public int ProjectBrowserTab { get; set; } = 1; // 0 = Projects, 1 = New Project
    public int SelectedRecentProject { get; set; } = -1;
    public int SelectedTemplate { get; set; } = 0;
    public int SelectedCategory { get; set; } = 0; // Blueprint vs C++

    public string ErrorMessage { get; set; } = "";

    public string CurrentProjectDir => ProjectManager.CurrentProjectDir;

    public bool TryCreateProject(string dirPath) => ProjectManager.TryCreateProject(dirPath);
    public bool TryOpenProject(string dirPath) => ProjectManager.TryOpenProject(dirPath);
}

