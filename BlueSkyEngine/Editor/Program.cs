using System;

namespace BlueSky.Editor;

public enum EditorState { ProjectBrowser, Workspace }

/// <summary>
/// Entry point for the BlueSky Engine Editor.
/// The editor logic is split across partial class files:
///   - EditorApp.cs         — State fields, Run(), main loop, RenderFrame(), Cleanup()
///   - ProjectBrowserUI.cs  — Project launcher/browser screen
///   - WorkspaceUI.cs       — Menu bar, toolbar, play controls, docking
///   - EditorPanels.cs      — Viewport, Outliner, Details, Content Browser, Console panels
///   - SceneCommands.cs     — Import, save/load, context menus, script editor, material/mesh editors
///   - PlayModeController.cs — Physics sync, terrain creation
///   - GizmoController.cs   — Translate/Rotate/Scale gizmo interaction
/// </summary>
partial class Program
{
    public static void Main(string[] args)
    {
        try   { Run(args); }
        catch (Exception ex)
        {
            var msg = $"[CRASH] {ex}";
            Console.Error.WriteLine(msg);
            System.IO.File.WriteAllText("bluesky_crash.log", msg);
        }
    }
}
