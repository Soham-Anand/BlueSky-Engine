using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Editor.UI;

namespace NotBSRenderer;

/// <summary>
/// Professional docking system with polished tab rendering, resizable splits,
/// and smooth visual transitions. Uses EditorTheme for consistent styling.
/// </summary>
public class DockingSystem
{
    // ── Sizing from theme ────────────────────────────────────────────
    private const float TabCloseSize = 14f;
    private const float TabPadding   = 12f;
    private const float TabGap       = 1f;

    // ── State ────────────────────────────────────────────────────────
    private DockNode _root;
    private readonly Dictionary<string, DockPanel> _panels = new();
    private DockPanel? _draggedPanel;
    private int _dragSplitterNode = -1;
    private float _dragSplitterStart;
    private float _dragMouseStart;
    private DockZone _hoveredZone;
    private DockNode? _hoveredZoneNode;
    private int _nextNodeId;
    private Vector2 _lastMousePos;
    private bool _lastMouseDown;

    public DockingSystem(float width, float height)
    {
        _root = new DockNode
        {
            Id = _nextNodeId++,
            Bounds = new DockRect(0, 0, width, height),
            Type = DockNodeType.Tabs
        };
    }

    // ── Public API ───────────────────────────────────────────────────

    public DockPanel AddPanel(string id, string title, Action<NotBSUI, DockRect> drawContent)
    {
        var panel = new DockPanel { Id = id, Title = title, DrawContent = drawContent };
        _panels[id] = panel;
        return panel;
    }

    public void DockTo(string panelId, DockPosition position)
    {
        if (!_panels.TryGetValue(panelId, out var panel)) return;
        DockToNode(_root, panel, position);
    }

    public void DockTo(string panelId, string targetPanelId, DockPosition position)
    {
        if (!_panels.TryGetValue(panelId, out var panel)) return;
        var targetNode = FindNodeWithPanel(_root, targetPanelId);
        if (targetNode != null)
            DockToNode(targetNode, panel, position);
        else
            DockToNode(_root, panel, position);
    }

    public void Resize(float width, float height, float offsetY = 0)
    {
        _root.Bounds = new DockRect(0, offsetY, width, height);
        LayoutNode(_root);
    }

    public void Update(NotBSUI ui, Vector2 mousePos, bool mouseDown)
    {
        bool mousePressed = mouseDown && !_lastMouseDown;
        bool mouseReleased = !mouseDown && _lastMouseDown;

        LayoutNode(_root);
        HandleSplitterDrag(mousePos, mouseDown, mousePressed, mouseReleased);
        RenderNode(ui, _root, mousePos, mouseDown, mousePressed, mouseReleased);

        _lastMousePos = mousePos;
        _lastMouseDown = mouseDown;
    }

    // ── Docking Logic ────────────────────────────────────────────────

    private void DockToNode(DockNode node, DockPanel panel, DockPosition position)
    {
        if (position == DockPosition.Center || node.Type == DockNodeType.Empty)
        {
            node.Type = DockNodeType.Tabs;
            if (!node.Tabs.Contains(panel.Id))
                node.Tabs.Add(panel.Id);
            node.ActiveTab = node.Tabs.Count - 1;
            return;
        }

        if (node.Type == DockNodeType.Split)
        {
            var wrapperNode = new DockNode { Id = _nextNodeId++, Type = DockNodeType.Split };
            wrapperNode.SplitDirection = node.SplitDirection;
            wrapperNode.SplitRatio = node.SplitRatio;
            wrapperNode.Bounds = node.Bounds;
            wrapperNode.Children = new List<DockNode>(node.Children);
            node.Children.Clear();

            var newPanelNode = new DockNode { Id = _nextNodeId++, Type = DockNodeType.Tabs };
            newPanelNode.Tabs.Add(panel.Id);
            newPanelNode.ActiveTab = 0;

            bool isVertical = position == DockPosition.Top || position == DockPosition.Bottom;

            if (isVertical)
            {
                node.SplitDirection = SplitDir.Vertical;
                node.SplitRatio = position == DockPosition.Bottom ? 0.65f : 0.35f;
                node.Children = position == DockPosition.Top
                    ? new List<DockNode> { newPanelNode, wrapperNode }
                    : new List<DockNode> { wrapperNode, newPanelNode };
            }
            else
            {
                node.SplitDirection = SplitDir.Horizontal;
                node.SplitRatio = 0.75f;
                node.Children = position == DockPosition.Left
                    ? new List<DockNode> { newPanelNode, wrapperNode }
                    : new List<DockNode> { wrapperNode, newPanelNode };
            }
            return;
        }

        var existingTabs = new List<string>(node.Tabs);
        int existingActive = node.ActiveTab;

        bool horizontal = position == DockPosition.Left || position == DockPosition.Right;
        node.Type = DockNodeType.Split;
        node.SplitDirection = horizontal ? SplitDir.Horizontal : SplitDir.Vertical;
        // UE5-like proportions: sidebars ~18%, bottom ~30%
        node.SplitRatio = (position == DockPosition.Left) ? 0.18f
                        : (position == DockPosition.Top)  ? 0.30f
                        : 0.18f; // placeholder for left/top
        node.Tabs.Clear();

        var panelNode = new DockNode { Id = _nextNodeId++, Type = DockNodeType.Tabs };
        panelNode.Tabs.Add(panel.Id);
        panelNode.ActiveTab = 0;

        var existingNode = new DockNode { Id = _nextNodeId++, Type = DockNodeType.Tabs };
        existingNode.Tabs.AddRange(existingTabs);
        existingNode.ActiveTab = existingActive;

        if (position == DockPosition.Left || position == DockPosition.Top)
        {
            node.Children = new List<DockNode> { panelNode, existingNode };
        }
        else
        {
            node.SplitRatio = (position == DockPosition.Right) ? 0.82f : 0.70f;
            node.Children = new List<DockNode> { existingNode, panelNode };
        }
    }

    // ── Layout ───────────────────────────────────────────────────────

    private void LayoutNode(DockNode node)
    {
        if (node.Type != DockNodeType.Split || node.Children.Count != 2) return;

        var b = node.Bounds;
        var c0 = node.Children[0];
        var c1 = node.Children[1];
        float sw = EditorTheme.SplitterW;

        if (node.SplitDirection == SplitDir.Horizontal)
        {
            float splitX = b.X + b.W * node.SplitRatio;
            c0.Bounds = new DockRect(b.X, b.Y, splitX - b.X - sw / 2, b.H);
            c1.Bounds = new DockRect(splitX + sw / 2, b.Y, b.X + b.W - splitX - sw / 2, b.H);
            node.SplitterRect = new DockRect(splitX - sw / 2, b.Y, sw, b.H);
        }
        else
        {
            float splitY = b.Y + b.H * node.SplitRatio;
            c0.Bounds = new DockRect(b.X, b.Y, b.W, splitY - b.Y - sw / 2);
            c1.Bounds = new DockRect(b.X, splitY + sw / 2, b.W, b.Y + b.H - splitY - sw / 2);
            node.SplitterRect = new DockRect(b.X, splitY - sw / 2, b.W, sw);
        }

        LayoutNode(c0);
        LayoutNode(c1);
    }

    // ── Splitter Interaction ─────────────────────────────────────────

    private void HandleSplitterDrag(Vector2 mouse, bool down, bool pressed, bool released)
    {
        if (released) { _dragSplitterNode = -1; return; }

        if (_dragSplitterNode >= 0 && down)
        {
            var node = FindNodeById(_root, _dragSplitterNode);
            if (node != null)
            {
                float delta = node.SplitDirection == SplitDir.Horizontal
                    ? mouse.X - _dragMouseStart
                    : mouse.Y - _dragMouseStart;
                float totalSize = node.SplitDirection == SplitDir.Horizontal ? node.Bounds.W : node.Bounds.H;
                float newRatio = _dragSplitterStart + delta / totalSize;
                node.SplitRatio = Math.Clamp(newRatio,
                    EditorTheme.MinPanelW / totalSize,
                    1f - EditorTheme.MinPanelW / totalSize);
            }
            return;
        }

        if (pressed)
        {
            var splitterNode = FindSplitterAt(_root, mouse);
            if (splitterNode != null)
            {
                _dragSplitterNode = splitterNode.Id;
                _dragSplitterStart = splitterNode.SplitRatio;
                _dragMouseStart = splitterNode.SplitDirection == SplitDir.Horizontal ? mouse.X : mouse.Y;
            }
        }
    }

    private DockNode? FindSplitterAt(DockNode node, Vector2 pos)
    {
        if (node.Type != DockNodeType.Split) return null;
        if (node.SplitterRect.Contains(pos)) return node;
        foreach (var child in node.Children)
        {
            var found = FindSplitterAt(child, pos);
            if (found != null) return found;
        }
        return null;
    }

    // ── Rendering ────────────────────────────────────────────────────

    private void RenderNode(NotBSUI ui, DockNode node, Vector2 mouse, bool down, bool pressed, bool released)
    {
        if (node.Type == DockNodeType.Split)
        {
            // ── Splitter line ──────────────────────────────────────
            bool splitterHot = node.SplitterRect.Contains(mouse) || _dragSplitterNode == node.Id;
            var r = node.SplitterRect;
            ui.Panel(r.X, r.Y, r.W, r.H, EditorTheme.Bg0);

            // Accent line centered in splitter when hovered
            if (splitterHot)
            {
                if (node.SplitDirection == SplitDir.Horizontal)
                    ui.Panel(r.X + r.W / 2 - 0.5f, r.Y + 8, 1, r.H - 16, EditorTheme.Accent);
                else
                    ui.Panel(r.X + 8, r.Y + r.H / 2 - 0.5f, r.W - 16, 1, EditorTheme.Accent);
            }

            foreach (var child in node.Children)
                RenderNode(ui, child, mouse, down, pressed, released);
            return;
        }

        if (node.Type == DockNodeType.Tabs && node.Tabs.Count > 0)
        {
            RenderTabbedPanel(ui, node, mouse, down, pressed, released);
        }
    }

    private void RenderTabbedPanel(NotBSUI ui, DockNode node, Vector2 mouse, bool down, bool pressed, bool released)
    {
        var b = node.Bounds;
        if (b.W < 1 || b.H < 1) return;

        float tabH = EditorTheme.TabH;

        // ── Check if active panel is transparent (3D viewport) ────
        bool isTransparent = false;
        if (node.ActiveTab >= 0 && node.ActiveTab < node.Tabs.Count)
        {
            string checkId = node.Tabs[node.ActiveTab];
            if (_panels.TryGetValue(checkId, out var checkPanel))
                isTransparent = checkPanel.Transparent;
        }

        // ── Panel background ─────────────────────────────────────
        if (!isTransparent)
            ui.Panel(b.X, b.Y + tabH, b.W, b.H - tabH, EditorTheme.Bg1);

        // ── Tab bar ──────────────────────────────────────────────
        ui.Panel(b.X, b.Y, b.W, tabH, EditorTheme.TabBarBg);
        
        // Bottom border of tab bar (skip for transparent panels like viewport)
        if (!isTransparent)
            ui.Panel(b.X, b.Y + tabH - 1, b.W, 1, EditorTheme.Border0);

        // ── Draw tabs ────────────────────────────────────────────
        float tabX = b.X + 4;
        for (int i = 0; i < node.Tabs.Count; i++)
        {
            string panelId = node.Tabs[i];
            if (!_panels.TryGetValue(panelId, out var panel)) continue;

            bool isActive = i == node.ActiveTab;
            float textWidth = panel.Title.Length * 7.2f;
            float tabWidth = textWidth + TabPadding * 2 + (node.Tabs.Count > 1 ? TabCloseSize + 6 : 0);
            tabWidth = Math.Max(tabWidth, 60);

            var tabRect = new DockRect(tabX, b.Y, tabWidth, tabH);
            bool tabHot = tabRect.Contains(mouse);

            // ── Tab background ──────────────────────────────────
            Vector4 tabColor;
            if (isActive)
                tabColor = EditorTheme.TabActive;
            else if (tabHot)
                tabColor = EditorTheme.TabHover;
            else
                tabColor = EditorTheme.TabInactive;

            ui.Panel(tabX, b.Y, tabWidth, tabH, tabColor);

            // ── Active indicator — 2px accent bar at top ────────
            if (isActive)
            {
                ui.Panel(tabX, b.Y, tabWidth, 2, EditorTheme.TabIndicator);
                // Erase bottom border so tab merges with content
                ui.Panel(tabX, b.Y + tabH - 1, tabWidth, 1, EditorTheme.TabActive);
            }

            // ── Right separator between tabs ────────────────────
            if (!isActive && i < node.Tabs.Count - 1)
            {
                ui.Panel(tabX + tabWidth, b.Y + 6, 1, tabH - 12, EditorTheme.Border1);
            }

            // ── Tab title ───────────────────────────────────────
            var titleColor = isActive ? EditorTheme.TabText : (tabHot ? EditorTheme.TextSecondary : EditorTheme.TabTextDim);

            // Panel icon
            string icon = GetPanelIcon(panelId);
            if (icon.Length > 0)
            {
                ui.SetCursor(tabX + 8, b.Y + tabH / 2 - 6);
                ui.Text(icon, isActive ? EditorTheme.Accent : EditorTheme.TextMuted);
                ui.SetCursor(tabX + 22, b.Y + tabH / 2 - 6);
            }
            else
            {
                ui.SetCursor(tabX + TabPadding, b.Y + tabH / 2 - 6);
            }
            ui.Text(panel.Title, titleColor);

            // ── Close button ────────────────────────────────────
            if (node.Tabs.Count > 1)
            {
                float closeX = tabX + tabWidth - TabCloseSize - 6;
                float closeY = b.Y + (tabH - TabCloseSize) / 2;
                var closeRect = new DockRect(closeX, closeY, TabCloseSize, TabCloseSize);
                bool closeHot = closeRect.Contains(mouse);

                if (closeHot)
                    ui.Panel(closeX - 1, closeY - 1, TabCloseSize + 2, TabCloseSize + 2, EditorTheme.Red);

                ui.SetCursor(closeX + 3, closeY + 1);
                ui.Text("×", closeHot ? EditorTheme.TextPrimary : EditorTheme.TextMuted);

                if (closeHot && pressed)
                {
                    node.Tabs.RemoveAt(i);
                    if (node.ActiveTab >= node.Tabs.Count)
                        node.ActiveTab = Math.Max(0, node.Tabs.Count - 1);
                    return;
                }
            }

            // ── Tab click ───────────────────────────────────────
            if (tabHot && pressed)
                node.ActiveTab = i;

            tabX += tabWidth + TabGap;
        }

        // ── Draw active panel content ────────────────────────────
        if (node.ActiveTab >= 0 && node.ActiveTab < node.Tabs.Count)
        {
            string activeId = node.Tabs[node.ActiveTab];
            if (_panels.TryGetValue(activeId, out var activePanel))
            {
                var contentRect = new DockRect(b.X, b.Y + tabH, b.W, b.H - tabH);
                activePanel.DrawContent?.Invoke(ui, contentRect);
            }
        }
    }

    /// <summary>Returns a small icon character for known panel types.</summary>
    private static string GetPanelIcon(string panelId)
    {
        return panelId switch
        {
            "viewport"       => "",   // Viewport gets no icon to save space
            "outliner"       => "◈",
            "details"        => "⚙",
            "content"        => "□",
            "console"        => "▸",
            _                => ""
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private DockNode? FindNodeWithPanel(DockNode node, string panelId)
    {
        if (node.Tabs.Contains(panelId)) return node;
        foreach (var child in node.Children)
        {
            var found = FindNodeWithPanel(child, panelId);
            if (found != null) return found;
        }
        return null;
    }

    private DockNode? FindNodeById(DockNode node, int id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            var found = FindNodeById(child, id);
            if (found != null) return found;
        }
        return null;
    }
}

// ── Data Types ───────────────────────────────────────────────────────

public struct DockRect
{
    public float X, Y, W, H;
    public DockRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
    public bool Contains(Vector2 p) => p.X >= X && p.X <= X + W && p.Y >= Y && p.Y <= Y + H;
    public override string ToString() => $"({X:F0},{Y:F0} {W:F0}x{H:F0})";
}

public enum DockPosition { Center, Left, Right, Top, Bottom }
public enum DockNodeType { Empty, Tabs, Split }
public enum SplitDir { Horizontal, Vertical }
public enum DockZone { None, Center, Left, Right, Top, Bottom }

public class DockNode
{
    public int Id;
    public DockNodeType Type;
    public DockRect Bounds;
    public List<string> Tabs = new();
    public int ActiveTab;
    public SplitDir SplitDirection;
    public float SplitRatio = 0.5f;
    public List<DockNode> Children = new();
    public DockRect SplitterRect;
}

public class DockPanel
{
    public string Id = "";
    public string Title = "";
    public Action<NotBSUI, DockRect>? DrawContent;
    public bool Transparent;
}
