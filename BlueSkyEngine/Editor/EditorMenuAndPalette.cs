using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using BlueSky.Editor.UI;
using BlueSky.Aegis.Input;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;
using BlueSky.LHO;

namespace BlueSky.Editor;

partial class Program
{
    // ── Menu Bar & Command Palette State ──────────────────────────────────
    internal static int _openMenuIndex = -1;
    private static readonly string[] MenuBarTitles = { "File", "Edit", "Window", "Tools", "Build", "Help" };
    private static readonly float[] _menuBarItemX = new float[6];
    private static readonly float[] _menuBarItemW = new float[6];

    internal static bool _showCommandPalette = false;
    internal static string _commandPaletteQuery = "";
    internal static int _commandPaletteSelectedIndex = 0;
    private static string _lastScoredQuery = "\0";
    private static int _lastPaletteWorldVersion = -1;
    private static readonly List<PaletteItem> _filteredPaletteItems = new();
    private static readonly List<PaletteItem> _allPaletteItems = new();

    // ── Avalanche macOS App Packager Modal State ──────────────────────────
    internal static bool _showAvalancheDialog = false;
    internal static string _avalancheAppName = "BlueSkyGame";
    internal static string _avalancheBundleId = "com.bluesky.game";
    internal static string _avalancheVersion = "1.0.0";
    internal static int _avalancheEditingField = 0; // 0=AppName, 1=BundleId, 2=Version, 3=SigningIdentity
    internal static int _avalancheResPreset = 0;    // 0=1280x720, 1=1600x900, 2=1920x1080
    internal static bool _avalancheFullscreen = false;
    internal static bool _avalancheVSync = true;
    internal static bool _avalancheShowHud = false;
    internal static bool _avalancheCodeSign = true;
    internal static AvalancheSigningMode _avalancheSigningMode = AvalancheSigningMode.Auto;
    // Empty = auto-detect a Developer ID Application certificate, otherwise sign ad-hoc.
    internal static string _avalancheSigningIdentity = "";
    internal static AvalancheBuildReport? _lastAvalancheReport = null;

    internal readonly struct MenuEntry
    {
        public readonly string Label;
        public readonly string Shortcut;
        public readonly Action? OnInvoke;
        public readonly Func<bool>? IsEnabled;
        public readonly Func<bool>? IsChecked;
        public readonly bool IsSeparator;

        private MenuEntry(string label, string shortcut, Action? onInvoke, Func<bool>? isEnabled, Func<bool>? isChecked, bool isSeparator)
        {
            Label = label;
            Shortcut = shortcut;
            OnInvoke = onInvoke;
            IsEnabled = isEnabled;
            IsChecked = isChecked;
            IsSeparator = isSeparator;
        }

        public static MenuEntry Item(string label, string shortcut, Action onInvoke, Func<bool>? isEnabled = null, Func<bool>? isChecked = null)
            => new(label, shortcut, onInvoke, isEnabled, isChecked, false);

        public static MenuEntry Separator()
            => new("", "", null, null, null, true);
    }

    internal sealed class PaletteItem
    {
        public string Category = "";
        public string Title = "";
        public string Subtitle = "";
        public string Shortcut = "";
        public Vector4 BadgeColor;
        public Action? Execute;
        public int Score;
    }

    internal static bool IsMenuOrPaletteOpen => _openMenuIndex >= 0 || _showCommandPalette || _showAvalancheDialog;

    internal static void ToggleCommandPalette()
    {
        _openMenuIndex = -1;
        _showAvalancheDialog = false;
        _showCommandPalette = !_showCommandPalette;
        if (_showCommandPalette)
        {
            _commandPaletteQuery = "";
            _commandPaletteSelectedIndex = 0;
            _lastScoredQuery = "\0";
            RebuildPaletteCatalog();
            RefreshFilteredPaletteItems();
        }
    }

    internal static void OpenCommandPalette()
    {
        _openMenuIndex = -1;
        _showAvalancheDialog = false;
        _showCommandPalette = true;
        _commandPaletteQuery = "";
        _commandPaletteSelectedIndex = 0;
        _lastScoredQuery = "\0";
        RebuildPaletteCatalog();
        RefreshFilteredPaletteItems();
    }

    internal static void CloseMenuAndPalette()
    {
        _openMenuIndex = -1;
        _showCommandPalette = false;
        _showAvalancheDialog = false;
    }

    // ── Keyboard Shortcuts & Command Palette Navigation ──────────────────
    internal static bool HandleMenuAndPaletteKeyDown(KeyCode k, ModifierKeys m)
    {
        bool cmdOrCtrl = m.HasFlag(ModifierKeys.Super) || m.HasFlag(ModifierKeys.Control);
        bool shift = m.HasFlag(ModifierKeys.Shift);

        // Cmd+P / Ctrl+P always toggles Command Palette in Workspace
        if (cmdOrCtrl && k == KeyCode.P && _state == EditorState.Workspace)
        {
            ToggleCommandPalette();
            return true;
        }

        // When Command Palette is open, capture navigation & execution keys
        if (_showCommandPalette)
        {
            if (k == KeyCode.Escape)
            {
                _showCommandPalette = false;
                return true;
            }
            if (k == KeyCode.Down)
            {
                if (_filteredPaletteItems.Count > 0)
                    _commandPaletteSelectedIndex = (_commandPaletteSelectedIndex + 1) % _filteredPaletteItems.Count;
                return true;
            }
            if (k == KeyCode.Up)
            {
                if (_filteredPaletteItems.Count > 0)
                    _commandPaletteSelectedIndex = (_commandPaletteSelectedIndex - 1 + _filteredPaletteItems.Count) % _filteredPaletteItems.Count;
                return true;
            }
            if (k == KeyCode.Enter || k == KeyCode.KeypadEnter)
            {
                if (_commandPaletteSelectedIndex >= 0 && _commandPaletteSelectedIndex < _filteredPaletteItems.Count)
                {
                    var chosen = _filteredPaletteItems[_commandPaletteSelectedIndex];
                    _showCommandPalette = false;
                    chosen.Execute?.Invoke();
                }
                return true;
            }
            return true; // Consume all other keys while typing in Command Palette
        }

        // When Avalanche Packager Modal is open, capture Escape / Tab / Enter & text input
        if (_showAvalancheDialog)
        {
            if (k == KeyCode.Escape)
            {
                _showAvalancheDialog = false;
                return true;
            }
            if (k == KeyCode.Tab)
            {
                _avalancheEditingField = (_avalancheEditingField + 1) % 4;
                return true;
            }
            if (k == KeyCode.Enter || k == KeyCode.KeypadEnter)
            {
                ExecuteAvalanchePackageCommand(launchAfterBuild: shift);
                return true;
            }
            return true;
        }

        // Escape closes open menu dropdown
        if (k == KeyCode.Escape && _openMenuIndex >= 0)
        {
            _openMenuIndex = -1;
            return true;
        }

        if (_state != EditorState.Workspace)
            return false;

        // Global Workspace Shortcuts (when no text modal is open)
        if (_showScriptEditor || _showRenameDialog || _showImportDialog)
            return false;

        if (cmdOrCtrl)
        {
            if (shift && k == KeyCode.B)
            {
                OpenAvalancheDialogCommand();
                return true;
            }
            if (k == KeyCode.S)
            {
                if (shift) SaveSceneAs();
                else SaveScene();
                return true;
            }
            if (k == KeyCode.O)
            {
                LoadScene();
                return true;
            }
            if (k == KeyCode.N)
            {
                if (shift) CreateEmptyEntityCommand();
                else NewScene();
                return true;
            }
            if (k == KeyCode.D)
            {
                DuplicateSelectedEntityCommand();
                return true;
            }
            if (k == KeyCode.R)
            {
                HotReloadScripts();
                _notificationSystem?.ShowInfo("Hot-reloaded TeaScripts", 2f);
                return true;
            }
            if (k == KeyCode.Backspace || k == KeyCode.Delete)
            {
                DeleteSelectedEntityCommand();
                return true;
            }
        }
        else
        {
            if (k == KeyCode.F5)
            {
                TogglePlayInEditorCommand();
                return true;
            }
            if (k == KeyCode.Delete)
            {
                DeleteSelectedEntityCommand();
                return true;
            }
            if (k == KeyCode.T && (_input == null || !_input.IsMouseButtonDown(MouseButton.Right)))
            {
                _terrainEditMode = !_terrainEditMode;
                _notificationSystem?.ShowInfo(_terrainEditMode ? "Terrain Sculpt Mode ON" : "Terrain Sculpt Mode OFF", 2f);
                return true;
            }
        }

        return false;
    }

    // ── Menu Bar Header Rendering (inside BuildWorkspaceUI) ──────────────
    internal static void DrawMenuBarHeader(EditorUI ui, float menuH)
    {
        float menuX = 144f;
        for (int i = 0; i < MenuBarTitles.Length; i++)
        {
            string m = MenuBarTitles[i];
            float itemW = ui.MeasureText(m) + 20f;
            _menuBarItemX[i] = menuX - 6f;
            _menuBarItemW[i] = itemW;

            bool isOpen = _openMenuIndex == i;
            bool menuHot = ui.IsHovering(menuX - 6f, 0f, itemW, menuH);

            // Hover-switch between menus once any menu is open
            if (_openMenuIndex >= 0 && menuHot && _openMenuIndex != i)
            {
                _openMenuIndex = i;
                isOpen = true;
            }

            if (isOpen || menuHot)
            {
                ui.RoundedPanel(menuX - 8f, 4f, itemW + 4f, menuH - 8f,
                    isOpen ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.22f) : EditorTheme.Bg3,
                    EditorTheme.SmallRadius);
                ui.Panel(menuX - 6f, menuH - 3f, itemW, 2f, EditorTheme.Accent);
            }

            // Invisible button to capture clicks on the menu header
            bool prevInput = ui.InputEnabled;
            ui.InputEnabled = true;
            if (ui.ClickableCard(menuX - 6f, 0f, itemW, menuH, 8100u + (uint)i,
                new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0)))
            {
                _showCommandPalette = false;
                _openMenuIndex = isOpen ? -1 : i;
            }
            ui.InputEnabled = prevInput;

            ui.SetCursor(menuX + 4f, menuH * 0.5f - 7f);
            ui.Text(m, (isOpen || menuHot) ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);
            menuX += itemW + 8f;
        }

        // Quick Command Palette search pill right in the menu bar ("⌘P Quick Action / Search...")
        float pillW = 220f;
        float pillH = 22f;
        float pillX = menuX + 14f;
        float pillY = (menuH - pillH) * 0.5f;

        bool prevEnabled = ui.InputEnabled;
        ui.InputEnabled = true;
        bool searchHot = ui.IsHovering(pillX, pillY, pillW, pillH);
        ui.RoundedPanel(pillX, pillY, pillW, pillH,
            _showCommandPalette ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.25f) :
            (searchHot ? EditorTheme.Bg3 : EditorTheme.Bg2),
            EditorTheme.PillRadius);
        EditorChrome.Stroke(ui, pillX, pillY, pillW, pillH,
            _showCommandPalette ? EditorTheme.Accent : (searchHot ? EditorTheme.Border2 : EditorTheme.Border1));

        ui.Icon(EditorUI.UIIcon.Search, pillX + 8f, pillY + 4f, 14f, searchHot ? EditorTheme.Accent : EditorTheme.TextMuted);
        ui.SetCursor(pillX + 26f, pillY + 4f);
        ui.Text("Search / Commands...", searchHot ? EditorTheme.TextPrimary : EditorTheme.TextMuted);

        string badge = OperatingSystem.IsMacOS() ? "Cmd+P" : "Ctrl+P";
        float badgeW = ui.MeasureText(badge) + 10f;
        ui.RoundedPanel(pillX + pillW - badgeW - 6f, pillY + 3f, badgeW, pillH - 6f, EditorTheme.Bg0, 3f);
        ui.SetCursor(pillX + pillW - badgeW - 1f, pillY + 4f);
        ui.Text(badge, EditorTheme.AccentCyan);

        if (ui.ClickableCard(pillX, pillY, pillW, pillH, 8120u,
            new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0)))
        {
            ToggleCommandPalette();
        }
        ui.InputEnabled = prevEnabled;
    }

    // ── Overlay Rendering for Menu Dropdowns & Command Palette ───────────
    internal static void DrawMenuAndPaletteOverlays(EditorUI ui, float windowW, float windowH)
    {
        if (_openMenuIndex >= 0 && _openMenuIndex < MenuBarTitles.Length)
        {
            DrawDropdownMenu(ui, _openMenuIndex, windowW, windowH);
        }

        if (_showCommandPalette)
        {
            DrawCommandPaletteModal(ui, windowW, windowH);
        }

        if (_showAvalancheDialog)
        {
            DrawAvalancheDialogModal(ui, windowW, windowH);
        }
    }

    private static void DrawDropdownMenu(EditorUI ui, int menuIndex, float windowW, float windowH)
    {
        var entries = GetMenuEntries(menuIndex);
        float dropX = _menuBarItemX[menuIndex];
        float dropY = EditorTheme.HeaderH + 2f;
        float dropW = 264f;
        float itemH = 28f;
        float sepH = 9f;

        float totalH = 10f;
        for (int i = 0; i < entries.Length; i++)
            totalH += entries[i].IsSeparator ? sepH : itemH;

        if (dropX + dropW > windowW - 8f)
            dropX = MathF.Max(8f, windowW - dropW - 8f);

        // Elevated surface with crisp border and top accent bar
        ui.Shadow(dropX + 3f, dropY + 4f, dropW, totalH, 4f, 6f, 0.45f);
        ui.RoundedPanel(dropX, dropY, dropW, totalH, EditorTheme.Bg2, EditorTheme.CardRadius);
        EditorChrome.Stroke(ui, dropX, dropY, dropW, totalH, EditorTheme.Border1);
        ui.Panel(dropX + 1f, dropY, dropW - 2f, 2f, EditorTheme.Accent);

        float curY = dropY + 5f;
        for (int i = 0; i < entries.Length; i++)
        {
            ref readonly var entry = ref entries[i];
            if (entry.IsSeparator)
            {
                ui.Panel(dropX + 10f, curY + 4f, dropW - 20f, 1f, EditorTheme.Border1);
                curY += sepH;
                continue;
            }

            bool enabled = entry.IsEnabled?.Invoke() ?? true;
            bool isChecked = entry.IsChecked?.Invoke() ?? false;
            bool rowHot = enabled && ui.IsHovering(dropX + 4f, curY, dropW - 8f, itemH);

            if (rowHot)
            {
                ui.RoundedPanel(dropX + 4f, curY, dropW - 8f, itemH, EditorTheme.HoverBg, EditorTheme.SmallRadius);
                ui.Panel(dropX + 4f, curY + 4f, 3f, itemH - 8f, EditorTheme.Accent);
            }

            if (ui.ClickableCard(dropX + 4f, curY, dropW - 8f, itemH, 8200u + (uint)(menuIndex * 32 + i),
                new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0)))
            {
                if (enabled)
                {
                    _openMenuIndex = -1;
                    entry.OnInvoke?.Invoke();
                    return;
                }
            }

            // Checkmark indicator if toggleable
            if (isChecked)
            {
                ui.Icon(EditorUI.UIIcon.Check, dropX + 10f, curY + 7f, 14f, EditorTheme.Green);
            }

            var labelColor = !enabled ? EditorTheme.TextDisabled : (rowHot ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);
            ui.SetCursor(dropX + 28f, curY + 7f);
            ui.Text(entry.Label, labelColor);

            if (!string.IsNullOrEmpty(entry.Shortcut))
            {
                float scW = ui.MeasureText(entry.Shortcut);
                ui.SetCursor(dropX + dropW - scW - 14f, curY + 7f);
                ui.Text(entry.Shortcut, enabled ? EditorTheme.TextMuted : EditorTheme.TextDisabled);
            }

            curY += itemH;
        }

        // Click outside to close menu
        if (_input != null && _input.IsMouseButtonPressed(MouseButton.Left))
        {
            var mp = ui.MousePosition;
            bool inMenuBar = mp.Y <= EditorTheme.HeaderH && mp.X >= _menuBarItemX[0] && mp.X <= _menuBarItemX[^1] + _menuBarItemW[^1];
            bool inDropdown = mp.X >= dropX && mp.X <= dropX + dropW && mp.Y >= dropY && mp.Y <= dropY + totalH;
            if (!inMenuBar && !inDropdown)
            {
                _openMenuIndex = -1;
            }
        }
    }

    private static MenuEntry[] GetMenuEntries(int menuIndex)
    {
        string mod = OperatingSystem.IsMacOS() ? "Cmd+" : "Ctrl+";
        bool hasSelectedEntity = _world != null && _selectedEntityId > 0 && _selectedEntityId < EditorOnlyTagComponent.EditorSystemIdBase;

        return menuIndex switch
        {
            // 0: FILE
            0 => new[]
            {
                MenuEntry.Item("New Scene", $"{mod}N", NewScene),
                MenuEntry.Item("Open Scene...", $"{mod}O", LoadScene),
                MenuEntry.Item("Save Scene", $"{mod}S", SaveScene),
                MenuEntry.Item("Save Scene As...", $"Shift+{mod}S", SaveSceneAs),
                MenuEntry.Separator(),
                MenuEntry.Item("Import Asset...", $"{mod}I", ImportFilesDialog),
                MenuEntry.Item("New TeaScript", "", CreateNewTeaScriptFromMenu),
                MenuEntry.Item("Refresh Content Browser", "", () =>
                {
                    Assets.RefreshContentBrowserListing();
                    Log("✓ Refreshed Content Browser");
                }),
                MenuEntry.Separator(),
                MenuEntry.Item("Switch Project...", "", SwitchToProjectBrowserCommand),
                MenuEntry.Item("Exit Editor", $"{mod}Q", () => _window?.Close())
            },

            // 1: EDIT
            1 => new[]
            {
                MenuEntry.Item("Command Palette...", $"{mod}P", OpenCommandPalette),
                MenuEntry.Separator(),
                MenuEntry.Item("Move Tool", "W", () =>
                {
                    if (_editorViewportRenderer != null)
                        _editorViewportRenderer.CurrentGizmoMode = ViewportRenderer.GizmoMode.Translate;
                }, isChecked: () => _editorViewportRenderer?.CurrentGizmoMode == ViewportRenderer.GizmoMode.Translate),
                MenuEntry.Item("Rotate Tool", "E", () =>
                {
                    if (_editorViewportRenderer != null)
                        _editorViewportRenderer.CurrentGizmoMode = ViewportRenderer.GizmoMode.Rotate;
                }, isChecked: () => _editorViewportRenderer?.CurrentGizmoMode == ViewportRenderer.GizmoMode.Rotate),
                MenuEntry.Item("Scale Tool", "R", () =>
                {
                    if (_editorViewportRenderer != null)
                        _editorViewportRenderer.CurrentGizmoMode = ViewportRenderer.GizmoMode.Scale;
                }, isChecked: () => _editorViewportRenderer?.CurrentGizmoMode == ViewportRenderer.GizmoMode.Scale),
                MenuEntry.Separator(),
                MenuEntry.Item("Create Empty Entity", $"Shift+{mod}N", CreateEmptyEntityCommand),
                MenuEntry.Item("Create Terrain", "", CreateTerrain),
                MenuEntry.Item("Duplicate Selected Entity", $"{mod}D", DuplicateSelectedEntityCommand, isEnabled: () => hasSelectedEntity),
                MenuEntry.Item("Delete Selected Entity", "Del", DeleteSelectedEntityCommand, isEnabled: () => hasSelectedEntity),
                MenuEntry.Item("Pack Selected to Prefab", "", PackEntityToPrefab, isEnabled: () => hasSelectedEntity)
            },

            // 2: WINDOW
            2 => new[]
            {
                MenuEntry.Item("Focus 3D Viewport", "", () => _dockingSystem?.FocusPanel("viewport", DockPosition.Center)),
                MenuEntry.Item("Focus World Outliner", "", () => _dockingSystem?.FocusPanel("outliner", DockPosition.Left)),
                MenuEntry.Item("Focus Inspector (Details)", "", () => _dockingSystem?.FocusPanel("details", DockPosition.Right)),
                MenuEntry.Item("Focus Content Browser", "", () => _dockingSystem?.FocusPanel("content", DockPosition.Bottom)),
                MenuEntry.Item("Focus Output Log", "", () => _dockingSystem?.FocusPanel("console", DockPosition.Bottom)),
                MenuEntry.Item("Focus Strata Configurator", "", () => _dockingSystem?.FocusPanel("strata", DockPosition.Right)),
                MenuEntry.Separator(),
                MenuEntry.Item("Performance Overlay", "F3", () => _showPerformanceOverlay = !_showPerformanceOverlay, isChecked: () => _showPerformanceOverlay),
                MenuEntry.Item("Reset Default Layout", "", ResetWorkspaceLayoutCommand)
            },

            // 3: TOOLS
            3 => new[]
            {
                MenuEntry.Item("Terrain Sculpt Mode", "T", () =>
                {
                    _terrainEditMode = !_terrainEditMode;
                    _notificationSystem?.ShowInfo(_terrainEditMode ? "Terrain Sculpt Mode ON" : "Terrain Sculpt Mode OFF", 2f);
                }, isChecked: () => _terrainEditMode),
                MenuEntry.Item("Physics Debug Wireframe", $"{mod}K", () => BlueSky.Pulse.PhysicsDebugVisualizer.Toggle(), isChecked: () => BlueSky.Pulse.PhysicsDebugVisualizer.IsEnabled),
                MenuEntry.Item("Studio Sky Environment", "F8", ToggleStudioSkyCommand, isChecked: () => BlueSky.Meridian.GlobalEnvironment.SkyPreset > 0.5f),
                MenuEntry.Item("Strata Demo Vehicle Paint", "F9", ToggleStrataDemoPaintCommand, isChecked: () => _editorViewportRenderer?.StrataOverride != null),
                MenuEntry.Separator(),
                MenuEntry.Item("View Mode: Lit", "", () => { _viewMode = 0; _debugView = 0; }, isChecked: () => _viewMode == 0 && _debugView == 0),
                MenuEntry.Item("View Mode: Wireframe", "", () => { _viewMode = 1; }, isChecked: () => _viewMode == 1),
                MenuEntry.Item("View Mode: Unlit", "", () => { _viewMode = 0; _debugView = 2; }, isChecked: () => _viewMode == 0 && _debugView == 2),
                MenuEntry.Item($"Cycle Shader Debug ({DebugViewNames[_debugView]})", "", () => _debugView = (_debugView + 1) % 8),
                MenuEntry.Separator(),
                MenuEntry.Item("Hot-Reload TeaScripts", $"{mod}R", () =>
                {
                    HotReloadScripts();
                    _notificationSystem?.ShowInfo("Hot-reloaded TeaScripts", 2f);
                }),
                MenuEntry.Item("Capture Strata Benchmark", $"{mod}B", CaptureBenchmarkCommand)
            },

            // 4: BUILD
            4 => new[]
            {
                MenuEntry.Item("Avalanche: Package macOS App (.app)...", $"Shift+{mod}B", OpenAvalancheDialogCommand),
                MenuEntry.Item("Avalanche: Quick Build & Launch macOS App", "", () => ExecuteAvalanchePackageCommand(launchAfterBuild: true)),
                MenuEntry.Item("Avalanche: Reveal Built Apps Folder", "", RevealAvalancheBuildsFolderCommand),
                MenuEntry.Separator(),
                MenuEntry.Item(_isPlaying ? "Stop Play in Editor" : "Play in Editor", "F5", TogglePlayInEditorCommand, isChecked: () => _isPlaying),
                MenuEntry.Item(_isPaused ? "Resume Play Mode" : "Pause Play Mode", "", () =>
                {
                    if (_isPlaying)
                    {
                        PlayMode.TogglePause(Log);
                        _notificationSystem?.ShowInfo(_isPaused ? "Paused" : "Resumed", 1.5f);
                    }
                }, isEnabled: () => _isPlaying, isChecked: () => _isPlaying && _isPaused),
                MenuEntry.Separator(),
                MenuEntry.Item((_standalonePlay?.IsRunning ?? false) ? "Stop Standalone Game" : "Launch Standalone Game", "", ToggleStandaloneCommand, isChecked: () => _standalonePlay?.IsRunning ?? false),
                MenuEntry.Item($"Multiplayer Instances: {_standaloneInstanceCount} (Cycle 1-4)", "", () =>
                {
                    _standaloneInstanceCount = (_standaloneInstanceCount % 4) + 1;
                    _notificationSystem?.ShowInfo($"Standalone instances set to {_standaloneInstanceCount}", 2f);
                }),
                MenuEntry.Separator(),
                MenuEntry.Item("Export Sample StrataPack", "", ExportSampleStrataPackCommand)
            },

            // 5: HELP
            5 => new[]
            {
                MenuEntry.Item("Command Palette & Search...", $"{mod}P", OpenCommandPalette),
                MenuEntry.Item("Run Engine Diagnostic Suite", "", RunDiagnosticsFromEditorCommand),
                MenuEntry.Separator(),
                MenuEntry.Item("About BlueSky Engine", "", () =>
                {
                    string rhiName = _rhi?.GetType().Name.Replace("Validation", "").Replace("Device", "") ?? "RHI";
                    Log($"✓ BlueSky Engine — {rhiName} | Ramanujan ({BlueSky.Ramanujan.RamanujanSimd.ActiveBackendName}) | Prism Forward+");
                })
            },

            _ => Array.Empty<MenuEntry>()
        };
    }

    // ── Command Palette Modal (`Cmd+P` / `Ctrl+P`) ───────────────────────
    private static void DrawCommandPaletteModal(EditorUI ui, float windowW, float windowH)
    {
        // Update search text from frame input
        bool queryChanged = false;
        if (_frameBackspacePressed && _commandPaletteQuery.Length > 0)
        {
            _commandPaletteQuery = _commandPaletteQuery[..^1];
            queryChanged = true;
        }
        if (!string.IsNullOrEmpty(_frameTypedText))
        {
            foreach (char c in _frameTypedText)
            {
                if (!char.IsControl(c))
                {
                    _commandPaletteQuery += c;
                    queryChanged = true;
                }
            }
        }

        int currentWorldVersion = _world != null ? (int)_world.Version : -1;
        if (_lastPaletteWorldVersion != currentWorldVersion)
        {
            RebuildPaletteCatalog();
            queryChanged = true;
        }

        if (queryChanged || _lastScoredQuery != _commandPaletteQuery)
        {
            RefreshFilteredPaletteItems();
        }

        // Dim background backdrop
        ui.Panel(0, 0, windowW, windowH, new Vector4(0f, 0f, 0f, 0.48f));

        float modalW = MathF.Min(620f, windowW - 40f);
        int maxVisibleRows = 10;
        float rowH = 34f;
        int visibleCount = Math.Min(_filteredPaletteItems.Count, maxVisibleRows);
        float listH = Math.Max(1, visibleCount) * rowH + 8f;
        float modalH = 54f + listH + 28f;
        float modalX = (windowW - modalW) * 0.5f;
        float modalY = MathF.Max(48f, windowH * 0.12f);

        // Modal container
        ui.Shadow(modalX + 4f, modalY + 6f, modalW, modalH, 6f, 10f, 0.55f);
        ui.RoundedPanel(modalX, modalY, modalW, modalH, EditorTheme.Bg1, EditorTheme.CardRadius);
        EditorChrome.Stroke(ui, modalX, modalY, modalW, modalH, EditorTheme.Border2);
        ui.Panel(modalX + 1f, modalY, modalW - 2f, 2f, EditorTheme.Accent);

        // Search input box
        float inputX = modalX + 12f;
        float inputY = modalY + 10f;
        float inputW = modalW - 24f;
        float inputH = 34f;
        ui.RoundedPanel(inputX, inputY, inputW, inputH, EditorTheme.Bg0, EditorTheme.InputRadius);
        EditorChrome.Stroke(ui, inputX, inputY, inputW, inputH, EditorTheme.Accent);
        ui.Icon(EditorUI.UIIcon.Search, inputX + 10f, inputY + 9f, 16f, EditorTheme.Accent);

        string cursor = (DateTime.UtcNow.Millisecond % 1000) < 500 ? "|" : "";
        if (string.IsNullOrEmpty(_commandPaletteQuery))
        {
            ui.SetCursor(inputX + 34f, inputY + 9f);
            ui.Text(cursor, EditorTheme.Accent);
            ui.SetCursor(inputX + 42f, inputY + 9f);
            ui.Text("Type a command, entity name, scene, or script...", EditorTheme.TextDisabled);
        }
        else
        {
            ui.SetCursor(inputX + 34f, inputY + 9f);
            ui.Text(_commandPaletteQuery + cursor, EditorTheme.TextPrimary);

            // Clear button
            if (ui.ButtonEx(inputX + inputW - 58f, inputY + 5f, 50f, 24f, "Clear",
                EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
                new Vector4(0, 0, 0, 0), EditorTheme.TextMuted, 8501u))
            {
                _commandPaletteQuery = "";
                RefreshFilteredPaletteItems();
            }
        }

        // Results list
        float listStartY = modalY + 50f;
        if (_filteredPaletteItems.Count == 0)
        {
            ui.SetCursor(modalX + 20f, listStartY + 12f);
            ui.Text("No matching commands, entities, or project assets found.", EditorTheme.TextMuted);
        }
        else
        {
            // Keep selected index in view window
            int startIdx = 0;
            if (_commandPaletteSelectedIndex >= maxVisibleRows)
                startIdx = _commandPaletteSelectedIndex - maxVisibleRows + 1;

            float ry = listStartY + 4f;
            for (int v = 0; v < visibleCount && (startIdx + v) < _filteredPaletteItems.Count; v++)
            {
                int idx = startIdx + v;
                var item = _filteredPaletteItems[idx];
                bool isSel = idx == _commandPaletteSelectedIndex;
                bool isHot = ui.IsHovering(modalX + 8f, ry, modalW - 16f, rowH - 2f);

                if (isHot && _input != null && _input.MouseDelta.LengthSquared() > 0.5f)
                {
                    _commandPaletteSelectedIndex = idx;
                    isSel = true;
                }

                if (isSel || isHot)
                {
                    ui.RoundedPanel(modalX + 8f, ry, modalW - 16f, rowH - 2f,
                        isSel ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.20f) : EditorTheme.HoverBg,
                        EditorTheme.SmallRadius);
                    if (isSel)
                        ui.Panel(modalX + 8f, ry + 5f, 3f, rowH - 12f, EditorTheme.Accent);
                }

                // Category pill badge
                float catW = MathF.Max(58f, ui.MeasureText(item.Category) + 14f);
                ui.RoundedPanel(modalX + 18f, ry + 6f, catW, 20f,
                    EditorTheme.WithAlpha(item.BadgeColor, 0.18f), 4f);
                ui.SetCursor(modalX + 25f, ry + 9f);
                ui.Text(item.Category, item.BadgeColor);

                // Main title + optional subtitle
                float titleX = modalX + 26f + catW;
                ui.SetCursor(titleX, ry + 9f);
                ui.Text(item.Title, isSel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);

                if (!string.IsNullOrEmpty(item.Subtitle))
                {
                    float titleW = ui.MeasureText(item.Title);
                    ui.SetCursor(titleX + titleW + 10f, ry + 9f);
                    ui.Text(item.Subtitle, EditorTheme.TextMuted);
                }

                // Right-aligned shortcut badge
                if (!string.IsNullOrEmpty(item.Shortcut))
                {
                    float scW = ui.MeasureText(item.Shortcut) + 12f;
                    float scX = modalX + modalW - scW - 16f;
                    ui.RoundedPanel(scX, ry + 6f, scW, 20f, EditorTheme.Bg0, 4f);
                    ui.SetCursor(scX + 6f, ry + 9f);
                    ui.Text(item.Shortcut, EditorTheme.TextMuted);
                }

                if (ui.ClickableCard(modalX + 8f, ry, modalW - 16f, rowH - 2f, 8600u + (uint)idx,
                    new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0)))
                {
                    _showCommandPalette = false;
                    item.Execute?.Invoke();
                    return;
                }

                ry += rowH;
            }
        }

        // Footer hint bar
        float footerY = modalY + modalH - 24f;
        ui.Panel(modalX + 1f, footerY - 2f, modalW - 2f, 1f, EditorTheme.Border1);
        ui.SetCursor(modalX + 16f, footerY + 3f);
        ui.Text("Up/Down: Navigate   Enter: Execute   Esc: Close", EditorTheme.TextDisabled);
        string countText = $"{_filteredPaletteItems.Count} matches";
        float countW = ui.MeasureText(countText);
        ui.SetCursor(modalX + modalW - countW - 16f, footerY + 3f);
        ui.Text(countText, EditorTheme.TextDisabled);

        // Click outside modal to close
        if (_input != null && _input.IsMouseButtonPressed(MouseButton.Left))
        {
            var mp = ui.MousePosition;
            bool insideModal = mp.X >= modalX && mp.X <= modalX + modalW && mp.Y >= modalY && mp.Y <= modalY + modalH;
            if (!insideModal)
            {
                _showCommandPalette = false;
            }
        }
    }

    internal static void RebuildPaletteCatalog()
    {
        _allPaletteItems.Clear();
        _lastPaletteWorldVersion = _world != null ? (int)_world.Version : -1;

        // 1. Add all Menu Bar commands
        for (int m = 0; m < MenuBarTitles.Length; m++)
        {
            string category = MenuBarTitles[m];
            Vector4 badgeColor = m switch
            {
                0 => EditorTheme.Accent,
                1 => EditorTheme.AccentCyan,
                2 => EditorTheme.Purple,
                3 => EditorTheme.Orange,
                4 => EditorTheme.Green,
                _ => EditorTheme.Yellow
            };

            var entries = GetMenuEntries(m);
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e.IsSeparator || e.OnInvoke == null) continue;
                if (e.Label.StartsWith("Command Palette", StringComparison.OrdinalIgnoreCase)) continue;

                var action = e.OnInvoke;
                var enabledCheck = e.IsEnabled;
                _allPaletteItems.Add(new PaletteItem
                {
                    Category = category,
                    Title = e.Label,
                    Subtitle = "",
                    Shortcut = e.Shortcut,
                    BadgeColor = badgeColor,
                    Execute = () =>
                    {
                        if (enabledCheck == null || enabledCheck())
                            action();
                        else
                            _notificationSystem?.ShowWarning($"'{e.Label}' requires an active selection or state.", 2f);
                    }
                });
            }
        }

        // 2. Add Scene Entities from _world
        if (_world != null)
        {
            string[] sysItems = { "DirectionalLight", "SkyAtmosphere", "PostProcessVolume" };
            for (int s = 0; s < sysItems.Length; s++)
            {
                uint sysId = EditorOnlyTagComponent.EditorSystemIdBase + (uint)s;
                string sysName = sysItems[s];
                _allPaletteItems.Add(new PaletteItem
                {
                    Category = "Entity",
                    Title = $"Select {sysName}",
                    Subtitle = "System Actor",
                    Shortcut = "",
                    BadgeColor = EditorTheme.Yellow,
                    Execute = () =>
                    {
                        _selectedEntityId = sysId;
                        _dockingSystem?.FocusPanel("details", DockPosition.Right);
                        Log($"Selected: {sysName}");
                    }
                });
            }

            foreach (var entity in _world.GetAllEntities())
            {
                if (_world.TryGetComponent<CameraComponent>(entity, out _))
                    continue;

                uint id = (uint)entity.Id;
                string displayName = $"Entity_{id}";
                if (_world.TryGetComponent<NameComponent>(entity, out var nameComp))
                {
                    string customName = nameComp.Name;
                    if (!string.IsNullOrWhiteSpace(customName))
                        displayName = $"{customName} (Entity_{id})";
                }

                string tags = "Actor";
                if (_world.HasComponent<TerrainComponent>(entity)) tags = "Terrain";
                else if (_world.HasComponent<SkeletalMeshComponent>(entity)) tags = "SkeletalMesh";
                else if (_world.HasComponent<StaticMeshComponent>(entity)) tags = "StaticMesh";
                else if (_world.HasComponent<TeaScriptComponent>(entity)) tags = "TeaScript";

                _allPaletteItems.Add(new PaletteItem
                {
                    Category = "Entity",
                    Title = $"Select {displayName}",
                    Subtitle = tags,
                    Shortcut = $"ID {id}",
                    BadgeColor = EditorTheme.Teal,
                    Execute = () =>
                    {
                        _selectedEntityId = id;
                        _dockingSystem?.FocusPanel("details", DockPosition.Right);
                        Log($"Selected {displayName}");
                    }
                });
            }
        }

        // 3. Add Project Scenes, Scripts, and Mesh Assets
        string? projDir = ProjectManager.CurrentProjectDir;
        if (!string.IsNullOrEmpty(projDir) && Directory.Exists(projDir))
        {
            try
            {
                string scenesDir = Path.Combine(projDir, "Scenes");
                if (Directory.Exists(scenesDir))
                {
                    foreach (var sceneFile in Directory.GetFiles(scenesDir, "*.blueskyscene", SearchOption.AllDirectories).Take(32))
                    {
                        string fileName = Path.GetFileName(sceneFile);
                        string captured = sceneFile;
                        _allPaletteItems.Add(new PaletteItem
                        {
                            Category = "Scene",
                            Title = $"Open Scene: {Path.GetFileNameWithoutExtension(sceneFile)}",
                            Subtitle = fileName,
                            Shortcut = "",
                            BadgeColor = EditorTheme.Accent,
                            Execute = () => LoadSceneFromPath(captured)
                        });
                    }
                }

                string assetsDir = ProjectManager.AssetsDir ?? Path.Combine(projDir, "Assets");
                if (Directory.Exists(assetsDir))
                {
                    foreach (var file in Directory.EnumerateFiles(assetsDir, "*.*", SearchOption.AllDirectories).Take(80))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        string name = Path.GetFileName(file);
                        string captured = file;

                        if (ext == ".tea")
                        {
                            _allPaletteItems.Add(new PaletteItem
                            {
                                Category = "Script",
                                Title = $"Edit TeaScript: {name}",
                                Subtitle = "Open in Script Editor",
                                Shortcut = "",
                                BadgeColor = EditorTheme.Green,
                                Execute = () => OpenScriptEditor(captured)
                            });
                        }
                        else if (ext == ".blueskyasset" || ext == ".glb" || ext == ".gltf" || ext == ".obj" || ext == ".fbx")
                        {
                            _allPaletteItems.Add(new PaletteItem
                            {
                                Category = "Asset",
                                Title = $"Spawn Asset: {Path.GetFileNameWithoutExtension(file)}",
                                Subtitle = name,
                                Shortcut = "",
                                BadgeColor = EditorTheme.Orange,
                                Execute = () => SpawnDraggedAsset(captured)
                            });
                        }
                    }
                }
            }
            catch
            {
                // Ignore transient filesystem errors during directory scan
            }
        }
    }

    internal static void RefreshFilteredPaletteItems()
    {
        _lastScoredQuery = _commandPaletteQuery;
        _filteredPaletteItems.Clear();

        string query = _commandPaletteQuery.Trim();
        if (string.IsNullOrEmpty(query))
        {
            for (int i = 0; i < _allPaletteItems.Count; i++)
            {
                _allPaletteItems[i].Score = 100 - Math.Min(i, 99);
                _filteredPaletteItems.Add(_allPaletteItems[i]);
            }
        }
        else
        {
            for (int i = 0; i < _allPaletteItems.Count; i++)
            {
                var item = _allPaletteItems[i];
                int score = ScoreCommandPaletteEntry(query, item.Category, item.Title, item.Subtitle, item.Shortcut);
                if (score > 0)
                {
                    item.Score = score;
                    _filteredPaletteItems.Add(item);
                }
            }
            _filteredPaletteItems.Sort((a, b) => b.Score.CompareTo(a.Score));
        }

        if (_commandPaletteSelectedIndex >= _filteredPaletteItems.Count)
            _commandPaletteSelectedIndex = Math.Max(0, _filteredPaletteItems.Count - 1);
    }

    /// <summary>
    /// Fast multi-token + subsequence fuzzy scorer for Command Palette entries.
    /// </summary>
    public static int ScoreCommandPaletteEntry(string query, string category, string title, string subtitle = "", string shortcut = "")
    {
        if (string.IsNullOrWhiteSpace(query))
            return 1;

        string q = query.Trim();
        string full = string.IsNullOrEmpty(subtitle) ? $"{category}: {title}" : $"{category}: {title} {subtitle}";

        // Exact prefix or substring on Title gets top priority
        if (title.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            return 1000 - title.Length;
        if (full.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            return 900 - full.Length;

        int titleIdx = title.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (titleIdx >= 0)
            return 800 - titleIdx * 2;

        int fullIdx = full.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (fullIdx >= 0)
            return 700 - fullIdx;

        if (!string.IsNullOrEmpty(shortcut) && shortcut.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 650;

        // Multi-token check: every space-separated token must match either as a substring or subsequence
        string[] tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 1)
        {
            int tokenScore = 500;
            for (int t = 0; t < tokens.Length; t++)
            {
                int pos = full.IndexOf(tokens[t], StringComparison.OrdinalIgnoreCase);
                if (pos < 0)
                {
                    tokenScore = 0;
                    break;
                }
                tokenScore -= pos;
            }
            if (tokenScore > 0)
                return Math.Max(100, tokenScore);
        }

        // Subsequence fuzzy match across `title` (e.g., "cemp" -> "Create Empty Entity")
        int qi = 0;
        int bonus = 0;
        int lastMatch = -2;
        for (int ti = 0; ti < title.Length && qi < q.Length; ti++)
        {
            if (char.ToLowerInvariant(title[ti]) == char.ToLowerInvariant(q[qi]))
            {
                if (ti == 0 || title[ti - 1] == ' ' || title[ti - 1] == ':' || title[ti - 1] == '_')
                    bonus += 25; // Word-boundary match bonus
                if (ti == lastMatch + 1)
                    bonus += 20; // Consecutive character bonus
                lastMatch = ti;
                qi++;
            }
        }

        if (qi == q.Length)
            return 150 + bonus - (title.Length - q.Length);

        return 0;
    }

    // ── Action Helpers for Menu Bar & Command Palette ────────────────────
    internal static void CreateEmptyEntityCommand()
    {
        if (_world == null) return;
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent
        {
            Position = new BlueSky.Ramanujan.Vector3(0, 1, 0),
            Rotation = BlueSky.Ramanujan.Quaternion.Identity,
            Scale = BlueSky.Ramanujan.Vector3.One
        });
        _selectedEntityId = (uint)entity.Id;
        _sceneDirty = true;
        Log($"✓ Created empty entity Entity_{entity.Id}");
    }

    internal static void DuplicateSelectedEntityCommand()
    {
        if (_world == null || _selectedEntityId == 0 || _selectedEntityId >= EditorOnlyTagComponent.EditorSystemIdBase)
            return;

        if (!_world.TryResolveEntity(_selectedEntityId, out var src) || !_world.IsEntityValid(src))
            return;

        var dst = _world.CreateEntity();
        if (_world.TryGetComponent<TransformComponent>(src, out var tr))
        {
            tr.Position = new BlueSky.Ramanujan.Vector3(tr.Position.X + 1.0f, tr.Position.Y, tr.Position.Z + 1.0f);
            _world.AddComponent(dst, tr);
        }
        if (_world.TryGetComponent<StaticMeshComponent>(src, out var sm))
            _world.AddComponent(dst, sm);
        if (_world.TryGetComponent<SkeletalMeshComponent>(src, out var skm))
            _world.AddComponent(dst, skm);
        if (_world.TryGetComponent<TeaScriptComponent>(src, out var ts))
        {
            ts.IsInitialized = false;
            ts.RuntimeInstance = 0;
            _world.AddComponent(dst, ts);
        }
        if (_world.TryGetComponent<PhysicsComponent>(src, out var phys))
            _world.AddComponent(dst, phys);
        if (_world.TryGetComponent<NameComponent>(src, out var nm))
        {
            var copyName = new NameComponent();
            copyName.SetName(nm.Name + "_Copy");
            _world.AddComponent(dst, copyName);
        }

        _selectedEntityId = (uint)dst.Id;
        _sceneDirty = true;
        Log($"✓ Duplicated Entity_{src.Id} → Entity_{dst.Id}");
    }

    internal static void DeleteSelectedEntityCommand()
    {
        if (_world == null || _selectedEntityId == 0 || _selectedEntityId >= EditorOnlyTagComponent.EditorSystemIdBase)
            return;

        if (!_world.TryResolveEntity(_selectedEntityId, out var entity) || !_world.IsEntityValid(entity))
            return;

        uint deletedId = _selectedEntityId;
        _world.DestroyEntity(entity);
        _selectedEntityId = 0;
        _sceneDirty = true;
        Log($"✓ Deleted Entity_{deletedId}");
    }

    private static void CreateNewTeaScriptFromMenu()
    {
        string targetDir = !string.IsNullOrEmpty(_currentBrowserDir) && Directory.Exists(_currentBrowserDir)
            ? _currentBrowserDir
            : (ProjectManager.AssetsDir ?? ProjectManager.CurrentProjectDir ?? "");

        if (string.IsNullOrEmpty(targetDir))
        {
            _notificationSystem?.ShowWarning("Open a project first to create a TeaScript", 2.5f);
            return;
        }

        Directory.CreateDirectory(targetDir);
        _contextMenuPath = targetDir;
        CreateNewTeaScript();
    }

    private static void SwitchToProjectBrowserCommand()
    {
        if (_isPlaying && _world != null)
        {
            PlayMode.Stop(_world, hotReloadScripts: HotReloadScripts, log: Log);
        }
        if (_sceneDirty)
        {
            SaveScene();
        }
        _openMenuIndex = -1;
        _showCommandPalette = false;
        _state = EditorState.ProjectBrowser;
        Log("Switched to Project Browser");
    }

    private static void ResetWorkspaceLayoutCommand()
    {
        if (_window == null) return;
        float w = _window.Size.X, h = _window.Size.Y;
        float headerH = EditorTheme.HeaderH + EditorTheme.ToolbarH;
        _dockingSystem = new DockingSystem(w, h - headerH);

        var vpPanel = _dockingSystem.AddPanel("viewport", "Viewport", DrawViewportPanel);
        vpPanel.Transparent = true;
        _dockingSystem.AddPanel("outliner", "Outliner", DrawOutlinerPanel);
        _dockingSystem.AddPanel("details", "Details", DrawDetailsPanel);
        _dockingSystem.AddPanel("content", "Content Browser", DrawContentBrowserPanel);
        _dockingSystem.AddPanel("console", "Output Log", DrawConsolePanel);

        _dockingSystem.DockTo("viewport", DockPosition.Center);
        _dockingSystem.DockTo("outliner", "viewport", DockPosition.Left);
        _dockingSystem.DockTo("details", "viewport", DockPosition.Right);
        _dockingSystem.DockTo("content", DockPosition.Bottom);
        _dockingSystem.DockTo("console", "content", DockPosition.Center);
        RegisterStrataPanel();

        _notificationSystem?.ShowSuccess("Workspace layout reset to default", 2f);
    }

    private static void ToggleStudioSkyCommand()
    {
        ref float preset = ref BlueSky.Meridian.GlobalEnvironment.SkyPreset;
        preset = preset < 0.5f ? 1f : 0f;
        _notificationSystem?.ShowInfo(preset > 0.5f
            ? "Studio sky ON (dark environment)." : "Studio sky OFF (day).", 2.5f);
    }

    private static void ToggleStrataDemoPaintCommand()
    {
        if (_editorViewportRenderer == null) return;
        if (_editorViewportRenderer.StrataOverride == null)
        {
            _editorViewportRenderer.StrataOverride =
                BlueSky.BSR.Strata.StrataSurfaceUpload.BuildDemoVehicleMaterial();
            _notificationSystem?.ShowInfo("Strata demo vehicle paint ON (Tier-1 lobes).", 2.5f);
        }
        else
        {
            _editorViewportRenderer.StrataOverride = null;
            _notificationSystem?.ShowInfo("Strata demo OFF (orange clay).", 2.5f);
        }
    }

    private static void CaptureBenchmarkCommand()
    {
        BlueSky.BSR.Strata.StrataBenchmark.LogReport("Menu-manual");
        string shotPath = Path.Combine(
            Directory.GetCurrentDirectory(), "Benchmarks",
            $"strata_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bmp");
        BlueSky.BSR.Strata.StrataBenchmark.RequestScreenshot(shotPath);
        _notificationSystem?.ShowInfo("Bench logged. Capturing screenshot…", 2.5f);
    }

    private static void TogglePlayInEditorCommand()
    {
        if (_world == null) return;
        if (!_isPlaying)
        {
            PlayMode.Start(
                _world,
                _terrainSystem,
                hotReloadScripts: HotReloadScripts,
                resetTeaScriptRuntimeInstances: () => _teaScriptSystem?.ResetRuntimeInstances(),
                log: Log
            );
            _physicsAccumulator = 0.0;
            _physicsStepsThisFrame = 0;
            _physicsFrame = 0;
            ResetEditorPhysicsInterpolation();
            _notificationSystem?.ShowSuccess("Play mode started", duration: 2f);
        }
        else
        {
            PlayMode.Stop(_world, hotReloadScripts: HotReloadScripts, log: Log);
            _physicsAccumulator = 0.0;
            _physicsStepsThisFrame = 0;
            _physicsFrame = 0;
            ResetEditorPhysicsInterpolation();
            _notificationSystem?.ShowInfo("Stopped - scene restored", duration: 2f);
        }
    }

    private static void ToggleStandaloneCommand()
    {
        bool standaloneRunning = _standalonePlay?.IsRunning ?? false;
        if (standaloneRunning)
        {
            _standalonePlay?.Stop();
            _notificationSystem?.ShowInfo("Standalone stopped", duration: 2f);
            return;
        }

        if (_world == null || string.IsNullOrEmpty(_currentScenePath))
        {
            _notificationSystem?.ShowWarning("No scene loaded — save scene first", duration: 3f);
            return;
        }

        NetworkingTeaScriptBridge.Register(null);
        bool launched = _standalonePlay?.Launch(_currentScenePath, 1280, 720, false, true) ?? false;
        if (launched)
            _notificationSystem?.ShowSuccess($"Standalone launched (PID: {_standalonePlay?.ProcessId})", duration: 3f);
        else
            _notificationSystem?.ShowError("Failed to launch standalone", duration: 3f);
    }

    private static void ExportSampleStrataPackCommand()
    {
        string outDir = ProjectManager.AssetsDir ?? ProjectManager.CurrentProjectDir ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, "SampleVehicle.stratapack");
        BlueSky.Tests.StrataPackTests.ExportSamplePack(outPath);
        Assets.RefreshContentBrowserListing();
        Log($"✓ Exported sample pack: {Path.GetFileName(outPath)}");
    }

    private static void RunDiagnosticsFromEditorCommand()
    {
        bool ok = BlueSky.Tests.RamanujanMathTests.RunAllTests() &&
                  BlueSky.Tests.PrismRenderTests.RunAllTests();
        if (ok)
            Log("✓ Engine Diagnostics: Ramanujan SIMD & Prism Render Suites PASSED (100%)");
        else
            Log("✗ Engine Diagnostics reported a failure — check console");
    }

    // ── Avalanche macOS App Packager Commands & UI Modal ─────────────────
    internal static void OpenAvalancheDialogCommand()
    {
        _openMenuIndex = -1;
        _showCommandPalette = false;

        string projName = !string.IsNullOrWhiteSpace(ProjectManager.CurrentProjectDir)
            ? Path.GetFileName(ProjectManager.CurrentProjectDir)
            : (!string.IsNullOrWhiteSpace(_currentScenePath)
                ? Path.GetFileNameWithoutExtension(_currentScenePath)
                : "BlueSkyGame");

        if (string.IsNullOrWhiteSpace(_avalancheAppName) || _avalancheAppName == "BlueSkyGame")
        {
            _avalancheAppName = string.IsNullOrWhiteSpace(projName) ? "BlueSkyGame" : projName;
            _avalancheBundleId = AvalanchePackager.SanitizeBundleIdentifier("", _avalancheAppName);
        }

        _avalancheEditingField = 0;
        _showAvalancheDialog = true;
    }

    internal static AvalancheBuildReport ExecuteAvalanchePackageCommand(bool launchAfterBuild)
    {
        AvalancheBuildReport Fail(string message)
        {
            var failed = new AvalancheBuildReport { Success = false, ErrorMessage = message };
            _lastAvalancheReport = failed;
            _notificationSystem?.ShowError($"Avalanche failed: {message}", 4f);
            return failed;
        }

        // While playing, the world is mid-simulation. Packaging it would embed that state instead of the authored scene.
        if (_isPlaying)
            return Fail("Stop Play mode before building");

        // Always write the current scene before copying it into the bundle, so the app gets what is
        // in the editor now rather than an older file on disk. If the save fails, stop instead of
        // packaging a stale scene.
        if (!string.IsNullOrEmpty(_currentScenePath) && !TrySaveScene())
            return Fail("Could not save the scene before building");

        var (resW, resH) = _avalancheResPreset switch
        {
            1 => (1600, 900),
            2 => (1920, 1080),
            _ => (1280, 720)
        };

        string projectDir = !string.IsNullOrWhiteSpace(ProjectManager.CurrentProjectDir)
            ? ProjectManager.CurrentProjectDir
            : Directory.GetCurrentDirectory();
        string outputDir = Path.Combine(projectDir, "Builds", "MacOS");

        var settings = new AvalancheBuildSettings
        {
            AppName = _avalancheAppName,
            BundleIdentifier = _avalancheBundleId,
            Version = _avalancheVersion,
            ProjectDirectory = projectDir,
            OutputDirectory = outputDir,
            StartupScenePath = _currentScenePath ?? "",
            WindowWidth = resW,
            WindowHeight = resH,
            Fullscreen = _avalancheFullscreen,
            VSync = _avalancheVSync,
            ShowDiagnosticsHud = _avalancheShowHud,
            AdHocCodeSign = _avalancheCodeSign,
            SigningMode = _avalancheSigningMode,
            SigningIdentity = _avalancheSigningIdentity
        };

        var report = AvalanchePackager.BuildMacOSApp(settings, _world, Log);
        _lastAvalancheReport = report;

        if (report.Success)
        {
            _notificationSystem?.ShowSuccess(
                $"Avalanche built {Path.GetFileName(report.AppBundlePath)} ({AvalanchePackager.FormatBytes(report.TotalBundleBytes)})",
                3.5f);

            if (launchAfterBuild)
            {
                AvalanchePackager.LaunchBuiltMacOSApp(report.AppBundlePath, Log);
            }
        }
        else
        {
            _notificationSystem?.ShowError($"Avalanche failed: {report.ErrorMessage}", 4f);
        }

        return report;
    }

    private static void RevealAvalancheBuildsFolderCommand()
    {
        string projectDir = !string.IsNullOrWhiteSpace(ProjectManager.CurrentProjectDir)
            ? ProjectManager.CurrentProjectDir
            : Directory.GetCurrentDirectory();
        string outputDir = Path.Combine(projectDir, "Builds", "MacOS");
        Directory.CreateDirectory(outputDir);

        if (_lastAvalancheReport is { Success: true } && Directory.Exists(_lastAvalancheReport.AppBundlePath))
            AvalanchePackager.RevealInFinder(_lastAvalancheReport.AppBundlePath);
        else
            AvalanchePackager.RevealInFinder(outputDir);

        Log($"✓ Revealed Builds folder: {outputDir}");
    }

    private static void DrawAvalancheDialogModal(EditorUI ui, float windowW, float windowH)
    {
        ref string activeText = ref _avalancheAppName;
        if (_avalancheEditingField == 1) activeText = ref _avalancheBundleId;
        else if (_avalancheEditingField == 2) activeText = ref _avalancheVersion;
        else if (_avalancheEditingField == 3) activeText = ref _avalancheSigningIdentity;

        if (_frameBackspacePressed && activeText.Length > 0)
        {
            activeText = activeText[..^1];
        }
        if (!string.IsNullOrEmpty(_frameTypedText))
        {
            foreach (char c in _frameTypedText)
            {
                if (!char.IsControl(c) && activeText.Length < 48)
                    activeText += c;
            }
        }

        ui.Panel(0, 0, windowW, windowH, new Vector4(0f, 0f, 0f, 0.54f));

        float modalW = MathF.Min(580f, windowW - 40f);
        float modalH = _lastAvalancheReport != null ? 518f : 468f;
        float modalX = (windowW - modalW) * 0.5f;
        float modalY = MathF.Max(40f, (windowH - modalH) * 0.5f);

        ui.Shadow(modalX + 4f, modalY + 6f, modalW, modalH, 6f, 12f, 0.58f);
        ui.RoundedPanel(modalX, modalY, modalW, modalH, EditorTheme.Bg1, EditorTheme.CardRadius);
        EditorChrome.Stroke(ui, modalX, modalY, modalW, modalH, EditorTheme.Border2);
        ui.Panel(modalX + 1f, modalY, modalW - 2f, 2f, EditorTheme.Accent);

        ui.SetCursor(modalX + 18f, modalY + 14f);
        ui.Text("AVALANCHE — MACOS APPLICATION PACKAGER (.APP)", EditorTheme.TextPrimary);
        string subHeader = "Bundles BlueSkyRuntime, Metal 3 shaders, Jolt & EOS dylibs, and cooked GameData";
        ui.SetCursor(modalX + 18f, modalY + 32f);
        ui.Text(subHeader, EditorTheme.TextMuted);

        if (ui.ButtonEx(modalX + modalW - 36f, modalY + 10f, 24f, 24f, "X",
            EditorTheme.Bg2, EditorTheme.Red, EditorTheme.Red,
            new Vector4(0, 0, 0, 0), EditorTheme.TextSecondary, 8800u))
        {
            _showAvalancheDialog = false;
            return;
        }

        ui.Panel(modalX + 14f, modalY + 54f, modalW - 28f, 1f, EditorTheme.Border1);

        string cursor = (DateTime.UtcNow.Millisecond % 1000) < 500 ? "|" : "";
        float labelX = modalX + 20f;
        float fieldX = modalX + 165f;
        float fieldW = modalW - 185f;
        float rowY = modalY + 66f;

        void DrawTextFieldRow(string label, string val, int fieldIdx, uint btnId, string placeholder = "")
        {
            bool focused = _avalancheEditingField == fieldIdx;
            ui.SetCursor(labelX, rowY + 6f);
            ui.Text(label, focused ? EditorTheme.AccentCyan : EditorTheme.TextSecondary);

            ui.RoundedPanel(fieldX, rowY, fieldW, 28f, EditorTheme.Bg0, EditorTheme.InputRadius);
            EditorChrome.Stroke(ui, fieldX, rowY, fieldW, 28f, focused ? EditorTheme.Accent : EditorTheme.Border1);
            ui.SetCursor(fieldX + 10f, rowY + 6f);
            if (focused)
            {
                ui.Text(val + cursor, EditorTheme.TextPrimary);
            }
            else if (string.IsNullOrEmpty(val) && !string.IsNullOrEmpty(placeholder))
            {
                ui.Text(placeholder, EditorTheme.TextDisabled);
            }
            else
            {
                ui.Text(val, EditorTheme.TextPrimary);
            }

            if (ui.ClickableCard(fieldX, rowY, fieldW, 28f, btnId,
                new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0)))
            {
                _avalancheEditingField = fieldIdx;
            }
            rowY += 36f;
        }

        DrawTextFieldRow("Application Name:", _avalancheAppName, 0, 8801u);
        DrawTextFieldRow("Bundle Identifier:", _avalancheBundleId, 1, 8802u);
        DrawTextFieldRow("Bundle Version:", _avalancheVersion, 2, 8803u);

        ui.SetCursor(labelX, rowY + 6f);
        ui.Text("Signing Identity:", EditorTheme.TextSecondary);
        string[] signModeLabels = { "Auto (Keychain)", "Developer ID", "Ad-Hoc (-)" };
        float signModeW = (fieldW - 12f) / 3f;
        for (int sm = 0; sm < 3; sm++)
        {
            var mode = sm == 0 ? AvalancheSigningMode.Auto : (sm == 1 ? AvalancheSigningMode.DeveloperId : AvalancheSigningMode.AdHoc);
            bool sel = _avalancheSigningMode == mode;
            float px = fieldX + sm * (signModeW + 6f);
            if (ui.ButtonEx(px, rowY, signModeW, 26f, signModeLabels[sm],
                sel ? EditorTheme.WithAlpha(EditorTheme.AccentCyan, 0.28f) : EditorTheme.Bg2,
                EditorTheme.Bg3, EditorTheme.AccentCyan,
                sel ? EditorTheme.AccentCyan : EditorTheme.Border1,
                sel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary, 8815u + (uint)sm))
            {
                _avalancheSigningMode = mode;
            }
        }
        rowY += 36f;

        if (_avalancheSigningMode != AvalancheSigningMode.AdHoc)
        {
            DrawTextFieldRow("Developer ID Cert:", _avalancheSigningIdentity, 3, 8804u, "(Auto-detect from Keychain)");
        }

        ui.SetCursor(labelX, rowY + 6f);
        ui.Text("Default Resolution:", EditorTheme.TextSecondary);
        string[] resLabels = { "1280 x 720 (HD)", "1600 x 900 (HD+)", "1920 x 1080 (FHD)" };
        float presetW = (fieldW - 12f) / 3f;
        for (int r = 0; r < 3; r++)
        {
            bool sel = _avalancheResPreset == r;
            float px = fieldX + r * (presetW + 6f);
            if (ui.ButtonEx(px, rowY, presetW, 26f, resLabels[r],
                sel ? EditorTheme.WithAlpha(EditorTheme.Accent, 0.28f) : EditorTheme.Bg2,
                EditorTheme.Bg3, EditorTheme.Accent,
                sel ? EditorTheme.Accent : EditorTheme.Border1,
                sel ? EditorTheme.TextPrimary : EditorTheme.TextSecondary, 8810u + (uint)r))
            {
                _avalancheResPreset = r;
            }
        }
        rowY += 36f;

        ui.SetCursor(labelX, rowY + 6f);
        ui.Text("Bundle Options:", EditorTheme.TextSecondary);
        float optW = (fieldW - 18f) / 4f;

        void DrawOptionToggle(int slot, string text, ref bool flag, uint id)
        {
            float ox = fieldX + slot * (optW + 6f);
            bool active = flag;
            if (ui.ButtonEx(ox, rowY, optW, 26f, text,
                active ? EditorTheme.WithAlpha(EditorTheme.Green, 0.22f) : EditorTheme.Bg2,
                EditorTheme.Bg3, EditorTheme.Green,
                active ? EditorTheme.Green : EditorTheme.Border1,
                active ? EditorTheme.Green : EditorTheme.TextMuted, id))
            {
                flag = !flag;
            }
        }

        DrawOptionToggle(0, "Fullscreen", ref _avalancheFullscreen, 8820u);
        DrawOptionToggle(1, "VSync", ref _avalancheVSync, 8821u);
        DrawOptionToggle(2, "F3 HUD On", ref _avalancheShowHud, 8822u);
        DrawOptionToggle(3, "CodeSign", ref _avalancheCodeSign, 8823u);
        rowY += 38f;

        string sceneLabel = !string.IsNullOrEmpty(_currentScenePath)
            ? Path.GetFileName(_currentScenePath)
            : "Active Editor World (auto-serialized to StartupScene.bsscene)";
        string projectDir = !string.IsNullOrWhiteSpace(ProjectManager.CurrentProjectDir)
            ? ProjectManager.CurrentProjectDir
            : Directory.GetCurrentDirectory();
        string appFolder = string.IsNullOrWhiteSpace(_avalancheAppName) ? "BlueSkyGame.app" : (_avalancheAppName.Trim() + ".app");
        string outPreview = Path.Combine(projectDir, "Builds", "MacOS", appFolder);

        ui.RoundedPanel(labelX, rowY, modalW - 40f, 46f, EditorTheme.Bg0, EditorTheme.SmallRadius);
        EditorChrome.Stroke(ui, labelX, rowY, modalW - 40f, 46f, EditorTheme.Border1);
        ui.SetCursor(labelX + 10f, rowY + 7f);
        ui.Text($"Startup Scene: {sceneLabel}", EditorTheme.AccentCyan);
        ui.SetCursor(labelX + 10f, rowY + 25f);
        ui.Text($"Output Bundle: {outPreview}", EditorTheme.TextMuted);
        rowY += 56f;

        if (_lastAvalancheReport != null)
        {
            var bannerColor = _lastAvalancheReport.Success ? EditorTheme.Green : EditorTheme.Red;
            ui.RoundedPanel(labelX, rowY, modalW - 40f, 38f, EditorTheme.WithAlpha(bannerColor, 0.14f), EditorTheme.SmallRadius);
            EditorChrome.Stroke(ui, labelX, rowY, modalW - 40f, 38f, bannerColor);
            ui.SetCursor(labelX + 10f, rowY + 11f);
            if (_lastAvalancheReport.Success)
            {
                string summary = $"✓ Built {Path.GetFileName(_lastAvalancheReport.AppBundlePath)} ({AvalanchePackager.FormatBytes(_lastAvalancheReport.TotalBundleBytes)}, scene: {Path.GetFileName(_lastAvalancheReport.StartupSceneRelativePath)} with {_lastAvalancheReport.StartupSceneEntityCount} entities) in {_lastAvalancheReport.DurationMs:F0} ms{(string.IsNullOrEmpty(_lastAvalancheReport.BundledDotnetRuntimeVersion) ? "; players need .NET 8 installed" : "")}";
                ui.Text(summary, EditorTheme.Green);
            }
            else
            {
                ui.Text($"✗ Build failed: {_lastAvalancheReport.ErrorMessage}", EditorTheme.Red);
            }
            rowY += 48f;
        }

        float btnY = modalY + modalH - 42f;
        float actionW = 148f;
        if (ui.ButtonEx(labelX, btnY, actionW, 30f, "Build macOS .app",
            EditorTheme.WithAlpha(EditorTheme.Accent, 0.28f), EditorTheme.Accent, EditorTheme.AccentDim,
            EditorTheme.Accent, EditorTheme.TextPrimary, 8830u))
        {
            ExecuteAvalanchePackageCommand(launchAfterBuild: false);
        }

        if (ui.ButtonEx(labelX + actionW + 10f, btnY, 168f, 30f, "Build & Launch .app",
            EditorTheme.WithAlpha(EditorTheme.Green, 0.25f), EditorTheme.Green, EditorTheme.Green,
            EditorTheme.Green, EditorTheme.TextPrimary, 8831u))
        {
            ExecuteAvalanchePackageCommand(launchAfterBuild: true);
        }

        if (ui.ButtonEx(labelX + actionW + 188f, btnY, 128f, 30f, "Reveal in Finder",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            EditorTheme.Border1, EditorTheme.TextSecondary, 8832u))
        {
            RevealAvalancheBuildsFolderCommand();
        }

        if (ui.ButtonEx(modalX + modalW - 92f, btnY, 72f, 30f, "Close",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            EditorTheme.Border1, EditorTheme.TextSecondary, 8833u))
        {
            _showAvalancheDialog = false;
        }
    }
}
