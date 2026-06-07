using System;
using System.Collections.Generic;
using System.Linq;
using BlueSky.Editor.UI;
using NotBSRenderer;

namespace BlueSky.Editor;

public enum CommandCategory
{
    Entity,
    Asset,
    Scene,
    View,
    Edit,
    System
}

public class Command
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public CommandCategory Category { get; set; }
    public Action Execute { get; set; } = () => {};
    public string[] Keywords { get; set; } = Array.Empty<string>();
    public string Icon { get; set; } = "•";
}

public class CommandPalette
{
    private bool _isOpen = false;
    private string _searchQuery = "";
    private List<Command> _commands = new();
    private List<Command> _filteredCommands = new();
    private int _selectedIndex = 0;
    private float _animProgress = 0f;
    
    public bool IsOpen => _isOpen;
    
    public CommandPalette()
    {
        RegisterDefaultCommands();
    }
    
    private void RegisterDefaultCommands()
    {
        _commands.Clear();
        
        // Entity commands
        Register(new Command
        {
            Name = "Create Empty Entity",
            Description = "Create a new empty entity in the scene",
            Category = CommandCategory.Entity,
            Icon = "⊕",
            Keywords = new[] { "new", "add", "entity", "object", "create" }
        });
        
        Register(new Command
        {
            Name = "Create Cube",
            Description = "Create a cube mesh entity",
            Category = CommandCategory.Entity,
            Icon = "◼",
            Keywords = new[] { "cube", "box", "mesh", "primitive" }
        });
        
        Register(new Command
        {
            Name = "Create Sphere",
            Description = "Create a sphere mesh entity",
            Category = CommandCategory.Entity,
            Icon = "●",
            Keywords = new[] { "sphere", "ball", "mesh", "primitive" }
        });
        
        Register(new Command
        {
            Name = "Create Light",
            Description = "Create a point light entity",
            Category = CommandCategory.Entity,
            Icon = "◉",
            Keywords = new[] { "light", "point", "illumination" }
        });
        
        Register(new Command
        {
            Name = "Create Camera",
            Description = "Create a camera entity",
            Category = CommandCategory.Entity,
            Icon = "◎",
            Keywords = new[] { "camera", "view", "perspective" }
        });
        
        // Scene commands
        Register(new Command
        {
            Name = "New Scene",
            Description = "Create a new empty scene",
            Category = CommandCategory.Scene,
            Icon = "⊞",
            Keywords = new[] { "new", "scene", "level", "map" }
        });
        
        Register(new Command
        {
            Name = "Save Scene",
            Description = "Save the current scene",
            Category = CommandCategory.Scene,
            Icon = "💾",
            Keywords = new[] { "save", "scene", "level" }
        });
        
        Register(new Command
        {
            Name = "Load Scene",
            Description = "Load an existing scene",
            Category = CommandCategory.Scene,
            Icon = "📂",
            Keywords = new[] { "load", "open", "scene", "level" }
        });
        
        // View commands
        Register(new Command
        {
            Name = "Focus Selected",
            Description = "Focus camera on selected entity",
            Category = CommandCategory.View,
            Icon = "🎯",
            Keywords = new[] { "focus", "frame", "zoom", "center" }
        });
        
        Register(new Command
        {
            Name = "Toggle Grid",
            Description = "Show/hide viewport grid",
            Category = CommandCategory.View,
            Icon = "⊞",
            Keywords = new[] { "grid", "toggle", "show", "hide" }
        });
        
        Register(new Command
        {
            Name = "Toggle Gizmos",
            Description = "Show/hide transform gizmos",
            Category = CommandCategory.View,
            Icon = "⊕",
            Keywords = new[] { "gizmo", "toggle", "transform" }
        });
        
        Register(new Command
        {
            Name = "Wireframe Mode",
            Description = "Toggle wireframe rendering",
            Category = CommandCategory.View,
            Icon = "◇",
            Keywords = new[] { "wireframe", "wire", "debug", "mesh" }
        });
        
        // Edit commands
        Register(new Command
        {
            Name = "Duplicate",
            Description = "Duplicate selected entity",
            Category = CommandCategory.Edit,
            Icon = "⊕",
            Keywords = new[] { "duplicate", "copy", "clone" }
        });
        
        Register(new Command
        {
            Name = "Delete",
            Description = "Delete selected entity",
            Category = CommandCategory.Edit,
            Icon = "✕",
            Keywords = new[] { "delete", "remove", "destroy" }
        });
        
        Register(new Command
        {
            Name = "Undo",
            Description = "Undo last operation",
            Category = CommandCategory.Edit,
            Icon = "↶",
            Keywords = new[] { "undo", "revert", "back" }
        });
        
        Register(new Command
        {
            Name = "Redo",
            Description = "Redo last undone operation",
            Category = CommandCategory.Edit,
            Icon = "↷",
            Keywords = new[] { "redo", "forward", "again" }
        });
        
        // System commands
        Register(new Command
        {
            Name = "Play",
            Description = "Start play mode",
            Category = CommandCategory.System,
            Icon = "▶",
            Keywords = new[] { "play", "run", "start", "test" }
        });
        
        Register(new Command
        {
            Name = "Stop",
            Description = "Stop play mode",
            Category = CommandCategory.System,
            Icon = "■",
            Keywords = new[] { "stop", "end", "exit" }
        });
        
        Register(new Command
        {
            Name = "Build Project",
            Description = "Build the project",
            Category = CommandCategory.System,
            Icon = "⚙",
            Keywords = new[] { "build", "compile", "export" }
        });
        
        Register(new Command
        {
            Name = "Project Settings",
            Description = "Open project settings",
            Category = CommandCategory.System,
            Icon = "⚙",
            Keywords = new[] { "settings", "preferences", "config" }
        });
    }
    
    public void Register(Command command)
    {
        _commands.Add(command);
    }
    
    public void Open()
    {
        _isOpen = true;
        _searchQuery = "";
        _selectedIndex = 0;
        _animProgress = 0f;
        UpdateFilter();
    }
    
    public void Close()
    {
        _isOpen = false;
        _searchQuery = "";
    }
    
    public void Toggle()
    {
        if (_isOpen) Close();
        else Open();
    }
    
    private void UpdateFilter()
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            _filteredCommands = _commands.ToList();
        }
        else
        {
            var query = _searchQuery.ToLower();
            _filteredCommands = _commands
                .Where(cmd => 
                    cmd.Name.ToLower().Contains(query) ||
                    cmd.Description.ToLower().Contains(query) ||
                    cmd.Keywords.Any(k => k.ToLower().Contains(query)))
                .OrderByDescending(cmd => 
                {
                    // Prioritize exact name matches
                    if (cmd.Name.ToLower().StartsWith(query)) return 1000;
                    if (cmd.Name.ToLower().Contains(query)) return 100;
                    if (cmd.Keywords.Any(k => k.ToLower() == query)) return 50;
                    return 1;
                })
                .ToList();
        }
        
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _filteredCommands.Count - 1));
    }
    
    public void Update(float deltaTime)
    {
        if (!_isOpen) return;
        
        // Smooth animation
        _animProgress = Math.Min(1f, _animProgress + deltaTime * 8f);
    }
    
    public void HandleInput(string typedText, bool backspace, bool upArrow, bool downArrow, bool enter, bool escape)
    {
        if (!_isOpen) return;
        
        if (escape)
        {
            Close();
            return;
        }
        
        if (enter && _filteredCommands.Count > 0)
        {
            _filteredCommands[_selectedIndex].Execute?.Invoke();
            Close();
            return;
        }
        
        if (upArrow)
        {
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
            return;
        }
        
        if (downArrow)
        {
            _selectedIndex = Math.Min(_filteredCommands.Count - 1, _selectedIndex + 1);
            return;
        }
        
        if (backspace && _searchQuery.Length > 0)
        {
            _searchQuery = _searchQuery.Substring(0, _searchQuery.Length - 1);
            UpdateFilter();
        }
        
        if (!string.IsNullOrEmpty(typedText))
        {
            _searchQuery += typedText;
            UpdateFilter();
        }
    }
    
    public void Render(NotBSUI ui, float windowWidth, float windowHeight)
    {
        if (!_isOpen) return;
        
        float paletteW = 600f;
        float paletteH = 400f;
        float paletteX = (windowWidth - paletteW) / 2f;
        float paletteY = 100f + (1f - _animProgress) * -50f; // Slide down animation
        
        // Backdrop overlay
        ui.Panel(0, 0, windowWidth, windowHeight, ModernTheme.WithAlpha(ModernTheme.Bg0, 0.7f * _animProgress));
        
        // Main palette panel
        ui.Panel(paletteX, paletteY, paletteW, paletteH, ModernTheme.Bg2);
        ui.Panel(paletteX, paletteY, paletteW, 2, ModernTheme.Accent);
        
        // Search box
        float searchH = 50f;
        ui.Panel(paletteX + 10, paletteY + 10, paletteW - 20, searchH, ModernTheme.Bg1);
        
        ui.SetCursor(paletteX + 20, paletteY + 20);
        ui.Text("🔍", ModernTheme.TextSecondary);
        
        ui.SetCursor(paletteX + 50, paletteY + 20);
        string displayText = string.IsNullOrEmpty(_searchQuery) ? "Type to search commands..." : _searchQuery;
        var textColor = string.IsNullOrEmpty(_searchQuery) ? ModernTheme.TextDisabled : ModernTheme.TextPrimary;
        ui.Text(displayText, textColor);
        
        // Results count
        ui.SetCursor(paletteX + paletteW - 100, paletteY + 20);
        ui.Text($"{_filteredCommands.Count} results", ModernTheme.TextMuted);
        
        // Results list
        float listY = paletteY + searchH + 20;
        float listH = paletteH - searchH - 30;
        float itemH = 50f;
        int maxVisible = (int)(listH / itemH);
        
        // Scroll to keep selected visible
        int scrollOffset = Math.Max(0, _selectedIndex - maxVisible + 1);
        
        for (int i = scrollOffset; i < Math.Min(_filteredCommands.Count, scrollOffset + maxVisible); i++)
        {
            var cmd = _filteredCommands[i];
            float itemY = listY + (i - scrollOffset) * itemH;
            bool isSelected = i == _selectedIndex;
            
            // Item background
            var bgColor = isSelected ? ModernTheme.SelectionBg : ModernTheme.Bg2;
            if (isSelected)
            {
                ui.Panel(paletteX + 5, itemY, paletteW - 10, itemH - 2, bgColor);
                ui.Panel(paletteX + 5, itemY, 3, itemH - 2, ModernTheme.Accent);
            }
            
            // Icon
            ui.SetCursor(paletteX + 20, itemY + 8);
            ui.Text(cmd.Icon, GetCategoryColor(cmd.Category));
            
            // Command name
            ui.SetCursor(paletteX + 50, itemY + 8);
            ui.Text(cmd.Name, isSelected ? ModernTheme.TextPrimary : ModernTheme.TextSecondary);
            
            // Description
            ui.SetCursor(paletteX + 50, itemY + 26);
            ui.Text(cmd.Description, ModernTheme.TextMuted);
            
            // Category badge
            string categoryText = cmd.Category.ToString();
            ui.SetCursor(paletteX + paletteW - 100, itemY + 18);
            ui.Text(categoryText, ModernTheme.TextDisabled);
        }
        
        // Hint text
        ui.SetCursor(paletteX + 10, paletteY + paletteH - 25);
        ui.Text("↑↓ Navigate  ⏎ Execute  Esc Close", ModernTheme.TextDisabled);
    }
    
    private System.Numerics.Vector4 GetCategoryColor(CommandCategory category)
    {
        return category switch
        {
            CommandCategory.Entity => ModernTheme.Green,
            CommandCategory.Asset => ModernTheme.Blue,
            CommandCategory.Scene => ModernTheme.Purple,
            CommandCategory.View => ModernTheme.Cyan,
            CommandCategory.Edit => ModernTheme.Yellow,
            CommandCategory.System => ModernTheme.Orange,
            _ => ModernTheme.TextSecondary
        };
    }
}
