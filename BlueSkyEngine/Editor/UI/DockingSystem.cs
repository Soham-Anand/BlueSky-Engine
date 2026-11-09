using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Editor.UI;

namespace BlueSky.Editor.UI;

/// <summary>
/// Studio docking system with rounded tab strips, glowing active tab indicators,
/// semantic panel accents, and interactive splitter grip handles.
/// </summary>
public class DockingSystem
{
    // ── Sizing from theme ────────────────────────────────────────────
    private const float TabCloseSize = 14f;
    private const float TabPadding   = 12f;
    private const float TabGap       = 2f;

    // ── State ────────────────────────────────────────────────────────
    private DockNode _root;
    private readonly Dictionary<string, DockPanel> _panels = new();
    private int _dragSplitterNode = -1;
    private float _dragSplitterStart;
    private float _dragMouseStart;
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

    public DockPanel AddPanel(string id, string title, Action<EditorUI, DockRect> drawContent)
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

    public bool FocusPanel(string panelId, DockPosition fallbackPosition = DockPosition.Center)
    {
        if (!_panels.TryGetValue(panelId, out var panel)) return false;
        var node = FindNodeWithPanel(_root, panelId);
        if (node != null)
        {
            int idx = node.Tabs.IndexOf(panelId);
            if (idx >= 0)
            {
                node.ActiveTab = idx;
                return true;
            }
        }
        DockToNode(_root, panel, fallbackPosition);
        return true;
    }

    public void Resize(float width, float height, float offsetY = 0, float offsetX = 0)
    {
        _root.Bounds = new DockRect(offsetX, offsetY, width, height);
        LayoutNode(_root);
    }

    public void Update(EditorUI ui, Vector2 mousePos, bool mouseDown)
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
                node.SplitRatio = position == DockPosition.Bottom ? 0.66f : 0.34f;
                node.Children = position == DockPosition.Top
                    ? new List<DockNode> { newPanelNode, wrapperNode }
                    : new List<DockNode> { wrapperNode, newPanelNode };
            }
            else
            {
                node.SplitDirection = SplitDir.Horizontal;
                node.SplitRatio = 0.76f;
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
        // Balanced studio proportions: left outliner ~19%, right inspector ~23%, bottom drawer ~31%
        node.SplitRatio = position == DockPosition.Top ? 0.30f : 0.19f;
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
            node.SplitRatio = (position == DockPosition.Right) ? 0.78f : 0.69f;
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
                if (!float.IsFinite(totalSize) || totalSize <= 0f) return;
                float newRatio = _dragSplitterStart + delta / totalSize;
                float minRatio = Math.Min(EditorTheme.MinPanelW / totalSize, 0.5f);
                node.SplitRatio = Math.Clamp(newRatio, minRatio, 1f - minRatio);
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
        // Expanded hit-test margin (6px) so thin splitters are effortless to grab
        var sr = node.SplitterRect;
        var hitRect = node.SplitDirection == SplitDir.Horizontal
            ? new DockRect(sr.X - 3f, sr.Y, sr.W + 6f, sr.H)
            : new DockRect(sr.X, sr.Y - 3f, sr.W, sr.H + 6f);
        if (hitRect.Contains(pos)) return node;
        foreach (var child in node.Children)
        {
            var found = FindSplitterAt(child, pos);
            if (found != null) return found;
        }
        return null;
    }

    // ── Rendering ────────────────────────────────────────────────────

    private void RenderNode(EditorUI ui, DockNode node, Vector2 mouse, bool down, bool pressed, bool released)
    {
        if (node.Type == DockNodeType.Split)
        {
            var r = node.SplitterRect;
            var hitRect = node.SplitDirection == SplitDir.Horizontal
                ? new DockRect(r.X - 3f, r.Y, r.W + 6f, r.H)
                : new DockRect(r.X, r.Y - 3f, r.W, r.H + 6f);
            bool splitterHot = hitRect.Contains(mouse) || _dragSplitterNode == node.Id;

            ui.Panel(r.X, r.Y, r.W, r.H, EditorTheme.SplitterNormal);

            // Subtle center grip pill + glowing accent bar when hovered or dragged
            if (splitterHot)
            {
                if (node.SplitDirection == SplitDir.Horizontal)
                {
                    ui.Panel(r.X, r.Y + 4, r.W, r.H - 8, EditorTheme.WithAlpha(EditorTheme.Accent, 0.35f));
                    ui.RoundedPanel(r.X, r.Y + r.H * 0.5f - 18f, r.W, 36f, EditorTheme.Accent, 1.5f);
                }
                else
                {
                    ui.Panel(r.X + 4, r.Y, r.W - 8, r.H, EditorTheme.WithAlpha(EditorTheme.Accent, 0.35f));
                    ui.RoundedPanel(r.X + r.W * 0.5f - 18f, r.Y, 36f, r.H, EditorTheme.Accent, 1.5f);
                }
            }
            else
            {
                if (node.SplitDirection == SplitDir.Horizontal && r.H > 40f)
                    ui.Panel(r.X + 1, r.Y + r.H * 0.5f - 10f, 1, 20f, EditorTheme.Border1);
                else if (node.SplitDirection == SplitDir.Vertical && r.W > 40f)
                    ui.Panel(r.X + r.W * 0.5f - 10f, r.Y + 1, 20f, 1, EditorTheme.Border1);
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

    private void RenderTabbedPanel(EditorUI ui, DockNode node, Vector2 mouse, bool down, bool pressed, bool released)
    {
        var b = node.Bounds;
        if (b.W < 1 || b.H < 1) return;

        float tabH = EditorTheme.TabH;

        // ── Check if active panel is transparent (3D viewport) ────
        bool isTransparent = false;
        string activeId = "";
        if (node.ActiveTab >= 0 && node.ActiveTab < node.Tabs.Count)
        {
            activeId = node.Tabs[node.ActiveTab];
            if (_panels.TryGetValue(activeId, out var checkPanel))
                isTransparent = checkPanel.Transparent;
        }

        // ── Panel background ─────────────────────────────────────
        if (!isTransparent)
        {
            ui.RoundedPanel(b.X, b.Y, b.W, b.H, EditorTheme.Bg1, EditorTheme.SmallRadius);
            EditorChrome.Stroke(ui, b.X, b.Y, b.W, b.H, EditorTheme.Border0);
        }

        // ── Tab bar ──────────────────────────────────────────────
        ui.GradientPanel(b.X, b.Y, b.W, tabH, EditorTheme.TabBarBg, EditorTheme.Bg0);
        ui.Panel(b.X, b.Y, b.W, 1, EditorTheme.Highlight);

        if (!isTransparent)
            ui.Panel(b.X, b.Y + tabH - 1, b.W, 1, EditorTheme.Border0);

        // ── Draw tabs ────────────────────────────────────────────
        float tabX = b.X + 6;
        for (int i = 0; i < node.Tabs.Count; i++)
        {
            string panelId = node.Tabs[i];
            if (!_panels.TryGetValue(panelId, out var panel)) continue;

            bool isActive = i == node.ActiveTab;
            var panelAccent = GetPanelAccent(panelId);
            float textWidth = ui.MeasureText(panel.Title);
            float tabWidth = textWidth + TabPadding * 2 + 12f + (node.Tabs.Count > 1 ? TabCloseSize + 6 : 0);
            tabWidth = Math.Max(tabWidth, 72);

            var tabRect = new DockRect(tabX, b.Y, tabWidth, tabH);
            bool tabHot = tabRect.Contains(mouse);

            Vector4 tabColor = isActive
                ? EditorTheme.TabActive
                : (tabHot ? EditorTheme.TabHover : EditorTheme.TabInactive);

            ui.RoundedPanel(tabX, b.Y + 3, tabWidth, tabH - 3, tabColor, 4f);

            // ── Active indicator — semantic accent bar at top ────
            if (isActive)
            {
                ui.RoundedPanel(tabX + 6, b.Y + 2, tabWidth - 12, 2.5f, panelAccent, 1.5f);
                ui.Panel(tabX, b.Y + tabH - 1, tabWidth, 1, EditorTheme.TabActive);
            }

            // ── Colored semantic dot + Tab title ────────────────
            ui.Circle(tabX + 12, b.Y + tabH * 0.5f + 1f, 3.5f,
                isActive ? panelAccent : EditorTheme.WithAlpha(panelAccent, 0.55f), filled: true);

            var titleColor = isActive ? EditorTheme.TabText : (tabHot ? EditorTheme.TextSecondary : EditorTheme.TabTextDim);
            ui.SetCursor(tabX + 22, b.Y + tabH * 0.5f - 6);
            ui.Text(panel.Title, titleColor);

            // ── Close button ────────────────────────────────────
            if (node.Tabs.Count > 1)
            {
                float closeX = tabX + tabWidth - TabCloseSize - 6;
                float closeY = b.Y + (tabH - TabCloseSize) / 2 + 1;
                var closeRect = new DockRect(closeX, closeY, TabCloseSize, TabCloseSize);
                bool closeHot = closeRect.Contains(mouse);

                if (closeHot)
                    ui.RoundedPanel(closeX - 1, closeY - 1, TabCloseSize + 2, TabCloseSize + 2, EditorTheme.WithAlpha(EditorTheme.Red, 0.35f), EditorTheme.SmallRadius);

                ui.SetCursor(closeX + 3, closeY);
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
        if (!string.IsNullOrEmpty(activeId) && _panels.TryGetValue(activeId, out var activePanel))
        {
            if (!isTransparent)
            {
                var accent = GetPanelAccent(activeId);
                ui.Panel(b.X, b.Y + tabH, b.W, 1.5f, EditorTheme.WithAlpha(accent, 0.55f));
            }

            var contentRect = new DockRect(b.X, b.Y + tabH, b.W, b.H - tabH);
            activePanel.DrawContent?.Invoke(ui, contentRect);
        }
    }

    private static Vector4 GetPanelAccent(string panelId)
    {
        return panelId switch
        {
            "viewport" => EditorTheme.Accent,
            "outliner" => EditorTheme.AccentCyan,
            "details"  => EditorTheme.Purple,
            "strata"   => EditorTheme.AccentWarm,
            "content"  => EditorTheme.Orange,
            "console"  => EditorTheme.Green,
            _          => EditorTheme.Accent
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
    public Action<EditorUI, DockRect>? DrawContent;
    public bool Transparent;
}
