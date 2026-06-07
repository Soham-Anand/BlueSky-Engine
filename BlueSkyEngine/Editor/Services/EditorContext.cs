using BlueSky.Core.ECS;
using BlueSky.Editor.UI;
using BlueSky.Platform;
using BlueSky.Platform.Input;
using NotBSRenderer;

namespace BlueSky.Editor.Services;

/// <summary>
/// Shared editor dependencies (window/input/RHI/world/UI) that services can use.
/// Keeps cross-cutting references out of the giant static Program state.
/// </summary>
public sealed class EditorContext
{
    public IWindow? Window { get; internal set; }
    public IInputContext? Input { get; internal set; }
    public IRHIDevice? Rhi { get; internal set; }
    public IRHISwapchain? Swapchain { get; internal set; }
    public NotBSUI? Ui { get; internal set; }
    public NotBSUIRenderer? UiRenderer { get; internal set; }

    public World? World { get; internal set; }

    public NotificationSystem? Notifications { get; internal set; }

    /// <summary>Centralized editor logging hook (wired to Program.Log).</summary>
    public Action<string>? Log { get; internal set; }
}

