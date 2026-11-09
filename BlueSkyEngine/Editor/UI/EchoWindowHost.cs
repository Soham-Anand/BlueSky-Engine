using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using BlueSky.Aegis;
using BlueSky.Aegis.Input;
using BlueSky.BSR.RHI;
using BlueSky.Echo;

namespace BlueSky.Editor.UI;

public enum EchoDocumentKind
{
    Source,
    Super
}

/// <summary>One open tab in the Echo window.</summary>
public sealed class EchoDocument
{
    public EchoDocumentKind Kind;
    public string Path = "";
    public string SourcePath = "";
    public EchoSourceAsset Source = null!;
    public EchoSuperAsset? Super;
    public bool Dirty;
    public int SelectedEffect = -1;

    public string DisplayName =>
        Kind == EchoDocumentKind.Source ? Source.Name : (Super?.Name ?? "Super");
}

/// <summary>
/// The Echo editor window: a real Cocoa window with its own swapchain and
/// immediate-mode UI, shared by Source and Super tabs.
/// </summary>
public sealed class EchoWindowHost : IDisposable
{
    private const float TabHeight = 38f;
    private const float Pad = 18f;

    private readonly IRHIDevice _device;
    private readonly IWindow _window;
    private readonly IInputContext _input;
    private readonly EditorUI _ui;
    private readonly EditorUIRenderer _renderer;
    private readonly IRHISwapchain _swapchain;
    private IRHITexture? _depth;
    private uint _depthW;
    private uint _depthH;

    private readonly List<EchoDocument> _documents = new();
    private int _activeTab;
    private bool _open;
    private double _time;
    private AudioSource? _audition;
    private string _status = "";
    private double _statusUntil;

    public IWindow Window => _window;
    public IInputContext Input => _input;
    public bool IsOpen => _open && _window.IsVisible && !_window.IsClosing;
    public bool HasDocuments => _documents.Count > 0;

    public EchoWindowHost(IRHIDevice device, FontAtlas fontAtlas, string title)
    {
        _device = device;

        var options = WindowOptions.Default;
        options.Title = title;
        options.Width = 980;
        options.Height = 720;
        options.X = 140;
        options.Y = 80;
        options.StartVisible = false;
        options.Resizable = true;

        _window = WindowFactory.Create(options);
        _input = _window.CreateInput();

        _ui = new EditorUI((uint)_window.Size.X, (uint)_window.Size.Y) { SharpCorners = false };
        _renderer = new EditorUIRenderer(device, slotBase: 3) { FontAtlas = fontAtlas };
        _renderer.Resize((int)_window.Size.X, (int)_window.Size.Y);

        _swapchain = device.CreateSwapchain(_window, PresentMode.Immediate);

        _window.Resize += size =>
        {
            _ui.Resize((uint)size.X, (uint)size.Y);
            _renderer.Resize((int)size.X, (int)size.Y);
        };
        _window.FramebufferResize += size => _swapchain.Resize((uint)size.X, (uint)size.Y);
        _window.Closing += () =>
        {
            _open = false;
            StopAudition();
            _window.Hide();
        };

        _input.KeyDown += (key, modifiers) =>
        {
            bool command = modifiers.HasFlag(ModifierKeys.Super) || modifiers.HasFlag(ModifierKeys.Control);
            if (command && key == KeyCode.S)
                SaveActiveDocument();
        };
    }

    public void BeginFrame()
    {
        _input.BeginFrame();
    }

    /// <summary>On macOS the shared event router already pumped every window.</summary>
    public void ProcessEvents()
    {
        if (!OperatingSystem.IsMacOS())
            _window.ProcessEvents();
    }

    public void ShowWindow()
    {
        if (!_window.IsVisible)
            _window.Show();
        _open = true;
    }

    public void FocusDocument(string path)
    {
        string resolved = ResolveFullPath(path);
        for (int i = 0; i < _documents.Count; i++)
        {
            if (string.Equals(_documents[i].Path, resolved, StringComparison.OrdinalIgnoreCase))
            {
                _activeTab = i;
                ShowWindow();
                return;
            }
        }

        OpenDocument(resolved);
    }

    public bool OpenDocument(string path)
    {
        string resolved = ResolveFullPath(path);
        string ext = Path.GetExtension(resolved);

        if (!File.Exists(resolved))
        {
            SetStatus($"File not found: {Path.GetFileName(resolved)}");
            return false;
        }

        for (int i = 0; i < _documents.Count; i++)
        {
            if (string.Equals(_documents[i].Path, resolved, StringComparison.OrdinalIgnoreCase))
            {
                _activeTab = i;
                ShowWindow();
                return true;
            }
        }

        if (ext.Equals(EchoSourceAsset.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var source = EchoSourceAsset.Load(resolved);
            if (source == null)
            {
                SetStatus($"Could not read Echo Source: {Path.GetFileName(resolved)}");
                return false;
            }

            _documents.Add(new EchoDocument
            {
                Kind = EchoDocumentKind.Source,
                Path = resolved,
                SourcePath = resolved,
                Source = source
            });
        }
        else if (ext.Equals(EchoSuperAsset.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var super = EchoSuperAsset.Load(resolved);
            if (super == null)
            {
                SetStatus($"Could not read Echo Super: {Path.GetFileName(resolved)}");
                return false;
            }

            var source = super.ResolveSource();
            if (source == null)
            {
                SetStatus($"Super references a missing Source: {Path.GetFileName(super.SourcePath)}");
                return false;
            }

            _documents.Add(new EchoDocument
            {
                Kind = EchoDocumentKind.Super,
                Path = resolved,
                SourcePath = ResolveFullPath(super.SourcePath),
                Source = source,
                Super = super
            });
        }
        else
        {
            SetStatus($"Not an Echo asset: {Path.GetFileName(resolved)}");
            return false;
        }

        _activeTab = _documents.Count - 1;
        ShowWindow();
        return true;
    }

    public void Render(float deltaTime)
    {
        if (!IsOpen)
            return;

        _time += deltaTime;
        _ui.Time = _time;
        _ui.InputEnabled = true;
        _ui.BeginFrame(
            _input.MousePosition,
            _input.IsMouseButtonDown(MouseButton.Left),
            "",
            false,
            _input.ScrollDelta.Y);

        DrawWindow();

        try
        {
            RenderWindow();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EchoWindow] Render failed: {ex.Message}");
        }
    }

    private void DrawWindow()
    {
        float w = _window.Size.X;
        float h = _window.Size.Y;

        _ui.Panel(0, 0, w, h, EditorTheme.Bg0);
        DrawTabBar(w);

        float contentY = TabHeight + 1;
        if (_documents.Count == 0 || _activeTab < 0 || _activeTab >= _documents.Count)
        {
            _ui.TextAt(Pad, contentY + 24, "No Echo asset open.", EditorTheme.TextMuted);
            return;
        }

        var doc = _documents[_activeTab];
        if (doc.Kind == EchoDocumentKind.Source)
            DrawSourceTab(doc, 0, contentY, w, h - contentY);
        else
            DrawSuperTab(doc, 0, contentY, w, h - contentY);

        DrawStatus(w, h);
    }

    private void DrawTabBar(float w)
    {
        _ui.Panel(0, 0, w, TabHeight, EditorTheme.Bg1);
        _ui.Panel(0, TabHeight - 1, w, 1, EditorTheme.Border0);

        float x = 8f;
        for (int i = 0; i < _documents.Count; i++)
        {
            var doc = _documents[i];
            float tabW = Math.Clamp(40f + _ui.MeasureText(doc.DisplayName) * 0.6f, 120f, 210f);
            bool active = i == _activeTab;

            if (_ui.ClickableCard(x, 5, tabW, TabHeight - 10, 8000u + (uint)i,
                active ? EditorTheme.Bg0 : EditorTheme.Bg2,
                EditorTheme.Bg3,
                EditorTheme.SelectionBg))
            {
                _activeTab = i;
            }

            if (active)
                _ui.Panel(x, TabHeight - 4, tabW, 2, EditorTheme.AccentCyan);

            _ui.TextAt(x + 10, 13, doc.DisplayName, active ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);
            if (doc.Dirty)
                _ui.Circle(x + tabW - 30, 18, 3.5f, EditorTheme.Orange, true);

            if (_ui.ButtonEx(x + tabW - 24, 9, 18, 18, "x",
                new Vector4(0, 0, 0, 0), EditorTheme.Red, EditorTheme.Red,
                new Vector4(0, 0, 0, 0), EditorTheme.TextMuted, 8100u + (uint)i))
            {
                CloseDocument(i);
                return;
            }

            x += tabW + 4f;
        }

        if (ActiveDocument is { Kind: EchoDocumentKind.Source } && _ui.ButtonEx(x + 4, 8, 96, 22, "+ New Super",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.AccentCyan, 8200))
        {
            CreateSuperFromActiveSource();
        }
    }

    private void DrawSourceTab(EchoDocument doc, float x, float y, float w, float h)
    {
        var source = doc.Source;
        float left = x + Pad;
        float top = y + Pad;
        float contentW = w - Pad * 2;

        _ui.TextAt(left, top, source.Name, EditorTheme.TextPrimary);
        _ui.TextAt(left, top + 22,
            $"{source.SampleRate} Hz  |  {source.Duration:F2}s  |  {source.FrameCount:N0} frames  |  stereo PCM",
            EditorTheme.TextMuted);

        DrawWaveform(left, top + 48, contentW, 132, source.Waveform, source);

        float cy = top + 196;
        float col1 = left;
        float col2 = left + contentW * 0.5f;

        float gain = source.Gain;
        if (SliderRow(col1, cy, "Gain", ref gain, 0f, 2f, 180f))
        {
            source.Gain = gain;
            MarkDirty(doc);
        }
        cy += 30;

        float trimStart = source.SampleRate > 0 ? (float)source.TrimStartFrame / source.SampleRate : 0f;
        if (SliderRow(col1, cy, "Trim Start", ref trimStart, 0f, source.Duration, 180f))
        {
            source.TrimStartFrame = (long)(trimStart * source.SampleRate);
            source.NormalizeRanges();
            MarkDirty(doc);
        }
        cy += 30;

        float trimEnd = source.SampleRate > 0 ? (float)source.TrimEndFrame / source.SampleRate : source.Duration;
        if (SliderRow(col1, cy, "Trim End", ref trimEnd, 0f, source.Duration, 180f))
        {
            source.TrimEndFrame = (long)(trimEnd * source.SampleRate);
            source.NormalizeRanges();
            MarkDirty(doc);
        }
        cy += 30;

        float loopStart = source.SampleRate > 0 ? (float)source.LoopStartFrame / source.SampleRate : 0f;
        if (SliderRow(col1, cy, "Loop Start", ref loopStart, 0f, source.Duration, 180f))
        {
            source.LoopStartFrame = (long)(loopStart * source.SampleRate);
            source.NormalizeRanges();
            MarkDirty(doc);
        }
        cy += 30;

        float loopEnd = source.SampleRate > 0 ? (float)source.LoopEndFrame / source.SampleRate : source.Duration;
        if (SliderRow(col1, cy, "Loop End", ref loopEnd, 0f, source.Duration, 180f))
        {
            source.LoopEndFrame = (long)(loopEnd * source.SampleRate);
            source.NormalizeRanges();
            MarkDirty(doc);
        }

        // Right column: output + spatial defaults.
        float ry = top + 196;
        _ui.TextAt(col2, ry, "Output", EditorTheme.TextPrimary);
        ry += 26;

        DrawChannelButtons(col2, ry, source.Channel, channel =>
        {
            source.Channel = channel;
            MarkDirty(doc);
        });
        ry += 32;

        _ui.TextAt(col2, ry + 2, "3D Spatial", EditorTheme.TextSecondary);
        if (_ui.ToggleSwitch(col2 + 110, ry, 40, 20, source.Is3D, EditorTheme.AccentCyan, 8300))
        {
            source.Is3D = !source.Is3D;
            MarkDirty(doc);
        }
        ry += 30;

        float minDist = source.MinDistance;
        if (SliderRow(col2, ry, "Min Dist", ref minDist, 0.01f, 50f, 150f))
        {
            source.MinDistance = minDist;
            source.MaxDistance = MathF.Max(minDist, source.MaxDistance);
            MarkDirty(doc);
        }
        ry += 30;

        float maxDist = source.MaxDistance;
        if (SliderRow(col2, ry, "Max Dist", ref maxDist, 1f, 500f, 150f))
        {
            source.MaxDistance = MathF.Max(source.MinDistance, maxDist);
            MarkDirty(doc);
        }
        ry += 30;

        DrawAttenuationButtons(col2, ry, source.AttenuationCurve, curve =>
        {
            source.AttenuationCurve = curve;
            MarkDirty(doc);
        });

        // Footer actions.
        float fy = y + h - 46;
        if (_ui.ButtonEx(left, fy, 104, 28, _audition != null ? "Stop" : "Audition",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.AccentCyan, 8400))
        {
            if (_audition != null) StopAudition();
            else StartAudition(doc);
        }

        if (_ui.ButtonEx(left + 112, fy, 116, 28, "Create Super",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.Green, 8401))
        {
            CreateSuperFromActiveSource();
        }

        if (_ui.ButtonEx(left + 236, fy, 88, 28, "Save",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 8402))
        {
            SaveDocument(doc);
        }
    }

    private void DrawSuperTab(EchoDocument doc, float x, float y, float w, float h)
    {
        var super = doc.Super!;
        var source = doc.Source;
        float left = x + Pad;
        float top = y + Pad;
        float contentW = w - Pad * 2;

        _ui.TextAt(left, top, super.Name, EditorTheme.TextPrimary);
        _ui.TextAt(left, top + 22, $"Live composite  |  {Path.GetFileName(super.SourcePath)}", EditorTheme.TextMuted);

        if (_ui.ButtonEx(left + contentW - 300, top - 2, 92, 24, "Open Source",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.AccentCyan, 8500))
        {
            FocusDocument(doc.SourcePath);
        }
        if (_ui.ButtonEx(left + contentW - 202, top - 2, 88, 24, _audition != null ? "Stop" : "Audition",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.AccentCyan, 8501))
        {
            if (_audition != null) StopAudition();
            else StartAudition(doc);
        }
        if (_ui.ButtonEx(left + contentW - 108, top - 2, 88, 24, "Save",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 8502))
        {
            SaveDocument(doc);
        }

        DrawWaveform(left, top + 50, contentW, 84, source.Waveform, source);

        float rackY = top + 152;
        float rackW = 272f;
        float listX = left;
        float listW = rackW;

        _ui.TextAt(listX, rackY, "Effect Rack", EditorTheme.TextPrimary);
        float rowY = rackY + 24;

        for (int i = 0; i < super.Effects.Count; i++)
        {
            var effect = super.Effects[i];
            bool selected = i == doc.SelectedEffect;

            _ui.Panel(listX, rowY, listW, 26, selected ? EditorTheme.SelectionBg : EditorTheme.Bg1, EditorTheme.Border0);

            if (_ui.ToggleSwitch(listX + 5, rowY + 4, 28, 18, effect.Enabled, EditorTheme.Green, 8600u + (uint)i * 10u))
            {
                effect.Enabled = !effect.Enabled;
                MarkDirty(doc);
            }

            if (_ui.ClickableCard(listX + 38, rowY + 2, listW - 74, 22, 8601u + (uint)i * 10u,
                new Vector4(0, 0, 0, 0), EditorTheme.WithAlpha(EditorTheme.Accent, 0.10f), EditorTheme.SelectionBg))
            {
                doc.SelectedEffect = i;
            }

            _ui.TextAt(listX + 42, rowY + 6, effect.DisplayName,
                effect.Enabled ? EditorTheme.TextPrimary : EditorTheme.TextMuted);

            if (_ui.ButtonEx(listX + listW - 24, rowY + 3, 18, 20, "x",
                new Vector4(0, 0, 0, 0), EditorTheme.Red, EditorTheme.Red,
                new Vector4(0, 0, 0, 0), EditorTheme.TextMuted, 8602u + (uint)i * 10u))
            {
                super.Effects.RemoveAt(i);
                doc.SelectedEffect = Math.Min(doc.SelectedEffect, super.Effects.Count - 1);
                MarkDirty(doc);
                break;
            }

            rowY += 28;
        }

        if (super.Effects.Count == 0)
            _ui.TextAt(listX + 4, rowY + 4, "No effects yet. Add one below.", EditorTheme.TextMuted);

        // Add-effect palette.
        float addY = rackY + 24 + Math.Max(1, super.Effects.Count) * 28 + 8;
        var types = (EchoEffectType[])Enum.GetValues(typeof(EchoEffectType));
        for (int i = 0; i < types.Length; i++)
        {
            int col = i % 2;
            int row = i / 2;
            float bx = listX + col * 136f;
            float by = addY + row * 26f;
            if (_ui.ButtonEx(bx, by, 128, 22, "+ " + EffectShortName(types[i]),
                EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
                new Vector4(0, 0, 0, 0), EditorTheme.TextSecondary, 8700u + (uint)i))
            {
                super.Effects.Add(EchoEffectSettings.CreateDefault(types[i]));
                doc.SelectedEffect = super.Effects.Count - 1;
                MarkDirty(doc);
            }
        }

        // Parameter editor.
        float px = left + rackW + 22;
        float pw = w - px - Pad;
        DrawEffectParameters(doc, px, rackY, pw);

        DrawSuperOutput(doc, px, rackY + 236, pw);

        float fy = y + h - 46;
        if (_ui.ButtonEx(left, fy, 104, 28, _audition != null ? "Stop" : "Audition",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.AccentCyan, 8800))
        {
            if (_audition != null) StopAudition();
            else StartAudition(doc);
        }
        if (_ui.ButtonEx(left + 112, fy, 88, 28, "Save",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.TextPrimary, 8801))
        {
            SaveDocument(doc);
        }
    }

    private void DrawEffectParameters(EchoDocument doc, float x, float y, float w)
    {
        var super = doc.Super!;
        if (doc.SelectedEffect < 0 || doc.SelectedEffect >= super.Effects.Count)
        {
            _ui.TextAt(x, y, "Select an effect to edit its parameters.", EditorTheme.TextMuted);
            return;
        }

        var effect = super.Effects[doc.SelectedEffect];
        _ui.TextAt(x, y, effect.DisplayName, EditorTheme.TextPrimary);

        if (_ui.ButtonEx(x + w - 190, y - 2, 60, 22, "Up",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.TextSecondary, 8900))
        {
            int i = doc.SelectedEffect;
            if (i > 0)
            {
                (super.Effects[i - 1], super.Effects[i]) = (super.Effects[i], super.Effects[i - 1]);
                doc.SelectedEffect = i - 1;
                MarkDirty(doc);
            }
        }
        if (_ui.ButtonEx(x + w - 126, y - 2, 60, 22, "Down",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.AccentDim,
            new Vector4(0, 0, 0, 0), EditorTheme.TextSecondary, 8901))
        {
            int i = doc.SelectedEffect;
            if (i < super.Effects.Count - 1)
            {
                (super.Effects[i + 1], super.Effects[i]) = (super.Effects[i], super.Effects[i + 1]);
                doc.SelectedEffect = i + 1;
                MarkDirty(doc);
            }
        }
        if (_ui.ButtonEx(x + w - 62, y - 2, 62, 22, "Remove",
            EditorTheme.Bg2, EditorTheme.Bg3, EditorTheme.Red,
            new Vector4(0, 0, 0, 0), EditorTheme.Red, 8902))
        {
            super.Effects.RemoveAt(doc.SelectedEffect);
            doc.SelectedEffect = Math.Min(doc.SelectedEffect, super.Effects.Count - 1);
            MarkDirty(doc);
            return;
        }

        float cy = y + 34;
        bool changed = false;

        switch (effect.Type)
        {
            case EchoEffectType.Gain:
                changed |= SliderRow(x, cy, "Gain (dB)", effect.GainDb, -24f, 24f, 170f, v => effect.GainDb = v); cy += 30;
                break;

            case EchoEffectType.Equalizer:
                changed |= SliderRow(x, cy, "Low (dB)", effect.LowGainDb, -18f, 18f, 170f, v => effect.LowGainDb = v); cy += 30;
                changed |= SliderRow(x, cy, "Mid (dB)", effect.MidGainDb, -18f, 18f, 170f, v => effect.MidGainDb = v); cy += 30;
                changed |= SliderRow(x, cy, "High (dB)", effect.HighGainDb, -18f, 18f, 170f, v => effect.HighGainDb = v); cy += 30;
                changed |= SliderRow(x, cy, "Mid Freq", effect.MidFrequency, 100f, 8000f, 170f, v => effect.MidFrequency = v); cy += 30;
                changed |= SliderRow(x, cy, "Mix", effect.Mix, 0f, 1f, 170f, v => effect.Mix = v); cy += 30;
                break;

            case EchoEffectType.LowPass:
            case EchoEffectType.HighPass:
                changed |= SliderRow(x, cy, "Cutoff", effect.Cutoff, 20f, 12000f, 170f, v => effect.Cutoff = v); cy += 30;
                changed |= SliderRow(x, cy, "Resonance", effect.Resonance, 0.1f, 8f, 170f, v => effect.Resonance = v); cy += 30;
                changed |= SliderRow(x, cy, "Mix", effect.Mix, 0f, 1f, 170f, v => effect.Mix = v); cy += 30;
                break;

            case EchoEffectType.Delay:
                changed |= SliderRow(x, cy, "Time (s)", effect.DelayTime, 0.01f, 1.5f, 170f, v => effect.DelayTime = v); cy += 30;
                changed |= SliderRow(x, cy, "Feedback", effect.Feedback, 0f, 0.95f, 170f, v => effect.Feedback = v); cy += 30;
                changed |= SliderRow(x, cy, "Mix", effect.Mix, 0f, 1f, 170f, v => effect.Mix = v); cy += 30;
                _ui.TextAt(x, cy + 2, "Ping-Pong", EditorTheme.TextSecondary);
                if (_ui.ToggleSwitch(x + 120, cy, 40, 20, effect.PingPong, EditorTheme.AccentCyan, 8910))
                {
                    effect.PingPong = !effect.PingPong;
                    changed = true;
                }
                cy += 30;
                break;

            case EchoEffectType.Reverb:
                changed |= SliderRow(x, cy, "Room Size", effect.RoomSize, 0f, 1f, 170f, v => effect.RoomSize = v); cy += 30;
                changed |= SliderRow(x, cy, "Damping", effect.Damping, 0f, 1f, 170f, v => effect.Damping = v); cy += 30;
                changed |= SliderRow(x, cy, "Width", effect.Width, 0f, 1f, 170f, v => effect.Width = v); cy += 30;
                changed |= SliderRow(x, cy, "Mix", effect.Mix, 0f, 1f, 170f, v => effect.Mix = v); cy += 30;
                break;

            case EchoEffectType.Compressor:
                changed |= SliderRow(x, cy, "Threshold", effect.ThresholdDb, -60f, 0f, 170f, v => effect.ThresholdDb = v); cy += 30;
                changed |= SliderRow(x, cy, "Ratio", effect.Ratio, 1f, 20f, 170f, v => effect.Ratio = v); cy += 30;
                changed |= SliderRow(x, cy, "Attack (ms)", effect.AttackMs, 0.1f, 100f, 170f, v => effect.AttackMs = v); cy += 30;
                changed |= SliderRow(x, cy, "Release (ms)", effect.ReleaseMs, 5f, 1000f, 170f, v => effect.ReleaseMs = v); cy += 30;
                changed |= SliderRow(x, cy, "Makeup (dB)", effect.MakeupDb, -12f, 12f, 170f, v => effect.MakeupDb = v); cy += 30;
                changed |= SliderRow(x, cy, "Mix", effect.Mix, 0f, 1f, 170f, v => effect.Mix = v); cy += 30;
                break;

            case EchoEffectType.Distortion:
                changed |= SliderRow(x, cy, "Drive", effect.Drive, 0f, 1f, 170f, v => effect.Drive = v); cy += 30;
                changed |= SliderRow(x, cy, "Tone", effect.Tone, 0f, 1f, 170f, v => effect.Tone = v); cy += 30;
                changed |= SliderRow(x, cy, "Mix", effect.Mix, 0f, 1f, 170f, v => effect.Mix = v); cy += 30;
                break;
        }

        if (changed)
            MarkDirty(doc);
    }

    private void DrawSuperOutput(EchoDocument doc, float x, float y, float w)
    {
        var super = doc.Super!;
        var output = super.Output;
        bool changed = false;

        _ui.Panel(x, y, w, 4, EditorTheme.Border0);
        _ui.TextAt(x, y + 12, "Output", EditorTheme.TextPrimary);
        float cy = y + 38;

        changed |= SliderRow(x, cy, "Gain", output.Gain, 0f, 2f, 150f, v => output.Gain = v); cy += 30;

        _ui.TextAt(x, cy + 2, "Channel", EditorTheme.TextSecondary);
        DrawChannelButtons(x + 80, cy, output.Channel, channel =>
        {
            output.Channel = channel;
            MarkDirty(doc);
        });
        cy += 32;

        _ui.TextAt(x, cy + 2, "Loop", EditorTheme.TextSecondary);
        if (_ui.ToggleSwitch(x + 120, cy, 40, 20, output.Loop, EditorTheme.Yellow, 8920))
        {
            output.Loop = !output.Loop;
            changed = true;
        }
        cy += 30;

        _ui.TextAt(x, cy + 2, "3D Spatial", EditorTheme.TextSecondary);
        if (_ui.ToggleSwitch(x + 120, cy, 40, 20, output.Is3D, EditorTheme.AccentCyan, 8921))
        {
            output.Is3D = !output.Is3D;
            changed = true;
        }
        cy += 30;

        changed |= SliderRow(x, cy, "Min Dist", output.MinDistance, 0.01f, 50f, 150f, v => output.MinDistance = v); cy += 30;
        changed |= SliderRow(x, cy, "Max Dist", output.MaxDistance, 1f, 500f, 150f, v => output.MaxDistance = v); cy += 30;

        _ui.TextAt(x, cy + 2, "Attenuation", EditorTheme.TextSecondary);
        DrawAttenuationButtons(x + 100, cy, output.AttenuationCurve, curve =>
        {
            output.AttenuationCurve = curve;
            MarkDirty(doc);
        });

        if (changed)
            MarkDirty(doc);
    }

    private void DrawWaveform(float x, float y, float w, float h, float[] peaks, EchoSourceAsset? source)
    {
        _ui.Panel(x, y, w, h, EditorTheme.Bg1, EditorTheme.Border1);

        if (source != null && source.FrameCount > 0)
        {
            float loopStart = (float)source.EffectiveLoopStart / source.FrameCount;
            float loopEnd = (float)source.EffectiveLoopEnd / source.FrameCount;
            _ui.Panel(x + loopStart * w, y, MathF.Max(1f, (loopEnd - loopStart) * w), h,
                EditorTheme.WithAlpha(EditorTheme.AccentCyan, 0.07f));
        }

        float mid = y + h * 0.5f;
        float amplitude = h * 0.5f - 5f;
        int columns = Math.Max(1, (int)(w / 2f));
        int peakCount = peaks?.Length ?? 0;

        if (peaks != null && peakCount > 0)
        {
            for (int c = 0; c < columns; c++)
            {
                int start = c * peakCount / columns;
                int end = Math.Max(start + 1, (c + 1) * peakCount / columns);
                float peak = 0f;
                for (int p = start; p < end && p < peakCount; p++)
                    peak = MathF.Max(peak, peaks[p]);

                float px = x + c * 2f;
                float py = MathF.Max(1f, peak * amplitude);
                _ui.Line(px, mid - py, px, mid + py, EditorTheme.WithAlpha(EditorTheme.AccentCyan, 0.75f));
            }
        }

        _ui.Line(x, mid, x + w, mid, EditorTheme.WithAlpha(EditorTheme.TextMuted, 0.22f));

        if (source != null && source.FrameCount > 0)
        {
            float trimStart = (float)source.EffectiveTrimStart / source.FrameCount;
            float trimEnd = (float)source.EffectiveTrimEnd / source.FrameCount;
            float loopStart = (float)source.EffectiveLoopStart / source.FrameCount;
            float loopEnd = (float)source.EffectiveLoopEnd / source.FrameCount;

            _ui.Line(x + trimStart * w, y, x + trimStart * w, y + h, EditorTheme.Yellow);
            _ui.Line(x + trimEnd * w, y, x + trimEnd * w, y + h, EditorTheme.Yellow);
            _ui.Line(x + loopStart * w, y, x + loopStart * w, y + h, EditorTheme.AccentCyan);
            _ui.Line(x + loopEnd * w, y, x + loopEnd * w, y + h, EditorTheme.AccentCyan);
        }
    }

    private void DrawChannelButtons(float x, float y, AudioChannel current, Action<AudioChannel> onSelect)
    {
        var channels = new[] { AudioChannel.SFX, AudioChannel.Music, AudioChannel.Ambience, AudioChannel.UI };
        for (int i = 0; i < channels.Length; i++)
        {
            var channel = channels[i];
            bool active = channel == current;
            if (_ui.ButtonEx(x + i * 66f, y, 62, 22, channel.ToString(),
                active ? EditorTheme.WithAlpha(EditorTheme.Yellow, 0.22f) : EditorTheme.Bg2,
                EditorTheme.Bg3, EditorTheme.AccentDim,
                new Vector4(0, 0, 0, 0),
                active ? EditorTheme.Yellow : EditorTheme.TextSecondary,
                9000u + (uint)i + (uint)(x * 7f)))
            {
                onSelect(channel);
            }
        }
    }

    private void DrawAttenuationButtons(float x, float y, DistanceAttenuationCurve current, Action<DistanceAttenuationCurve> onSelect)
    {
        var curves = (DistanceAttenuationCurve[])Enum.GetValues(typeof(DistanceAttenuationCurve));
        for (int i = 0; i < curves.Length; i++)
        {
            var curve = curves[i];
            bool active = curve == current;
            if (_ui.ButtonEx(x + i * 74f, y, 70, 22, curve.ToString(),
                active ? EditorTheme.WithAlpha(EditorTheme.AccentCyan, 0.22f) : EditorTheme.Bg2,
                EditorTheme.Bg3, EditorTheme.AccentDim,
                new Vector4(0, 0, 0, 0),
                active ? EditorTheme.AccentCyan : EditorTheme.TextSecondary,
                9100u + (uint)i + (uint)(x * 5f)))
            {
                onSelect(curve);
            }
        }
    }

    private bool SliderRow(float x, float y, string label, ref float value, float min, float max, float width)
    {
        _ui.TextAt(x, y + 2, label, EditorTheme.TextSecondary);
        _ui.SetCursor(x + 100, y);
        return _ui.Slider(ref value, min, max, width, 18);
    }

    /// <summary>Slider bound to a property: the UI edits a local copy and the
    /// setter writes it back only when the value changed.</summary>
    private bool SliderRow(float x, float y, string label, float value, float min, float max, float width, Action<float> setter)
    {
        _ui.TextAt(x, y + 2, label, EditorTheme.TextSecondary);
        _ui.SetCursor(x + 100, y);
        if (!_ui.Slider(ref value, min, max, width, 18))
            return false;

        setter(value);
        return true;
    }

    private void DrawStatus(float w, float h)
    {
        if (string.IsNullOrEmpty(_status) || _time > _statusUntil)
            return;

        _ui.TextAt(Pad, h - 18, _status, EditorTheme.Green);
    }

    private void SetStatus(string message)
    {
        _status = message;
        _statusUntil = _time + 4.0;
        Console.WriteLine($"[EchoWindow] {message}");
    }

    private EchoDocument? ActiveDocument =>
        _activeTab >= 0 && _activeTab < _documents.Count ? _documents[_activeTab] : null;

    private void MarkDirty(EchoDocument doc)
    {
        doc.Dirty = true;
        doc.Super?.Touch();

        if (_audition != null && doc.Super != null)
            _audition.Super = doc.Super;
    }

    private void CreateSuperFromActiveSource()
    {
        var doc = ActiveDocument;
        if (doc is not { Kind: EchoDocumentKind.Source })
            return;

        string path = EchoSuperAsset.DefaultPathFor(doc.Path);
        if (File.Exists(path))
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(path);
            int n = 2;
            while (File.Exists(path))
                path = Path.Combine(dir, $"{baseName} {n++}{EchoSuperAsset.Extension}");
        }

        var super = EchoSuperAsset.CreateForSource(
            doc.Source,
            doc.Path,
            BlueSky.Editor.ProjectManager.CurrentProjectDir);

        if (!super.Save(path))
        {
            SetStatus("Could not save the new Super.");
            return;
        }

        _documents.Add(new EchoDocument
        {
            Kind = EchoDocumentKind.Super,
            Path = path,
            SourcePath = doc.Path,
            Source = doc.Source,
            Super = super
        });
        _activeTab = _documents.Count - 1;
        BlueSky.Editor.Program.Assets.RefreshContentBrowserListing();
        SetStatus($"Created {Path.GetFileName(path)}");
    }

    private void SaveActiveDocument()
    {
        var doc = ActiveDocument;
        if (doc != null)
            SaveDocument(doc);
    }

    private void SaveDocument(EchoDocument doc)
    {
        bool ok;
        if (doc.Kind == EchoDocumentKind.Source)
        {
            ok = doc.Source.Save(doc.Path);
        }
        else
        {
            if (!string.IsNullOrEmpty(doc.Super!.SourcePath))
                doc.Super.SourcePath = ToProjectRelative(doc.SourcePath);
            ok = doc.Super.Save(doc.Path);
        }

        if (ok)
        {
            doc.Dirty = false;
            BlueSky.Editor.Program.Assets.RefreshContentBrowserListing();
            SetStatus($"Saved {Path.GetFileName(doc.Path)}");
        }
        else
        {
            SetStatus($"Save failed: {Path.GetFileName(doc.Path)}");
        }
    }

    private void StartAudition(EchoDocument doc)
    {
        StopAudition();

        var echo = BlueSky.Editor.Program._echo;
        if (echo == null)
        {
            SetStatus("Echo audio is unavailable.");
            return;
        }

        string clipName = "echo-audition:" + doc.Path;
        var clip = echo.LoadClip(clipName, doc.Path, doc.Super, doc.Source);
        if (clip == null)
        {
            SetStatus("Could not load the audition clip.");
            return;
        }

        var source = echo.PlaySound(
            clipName,
            Vector3.Zero,
            volume: 1.0f,
            loop: true,
            channel: doc.Kind == EchoDocumentKind.Super ? doc.Super!.Output.Channel : AudioChannel.SFX,
            is3D: false);

        if (source == null)
        {
            SetStatus("Could not start audition.");
            return;
        }

        source.Super = doc.Super;
        source.Loop = true;
        source.IgnoresGamePause = true; // an audition is not a game sound
        _audition = source;
        SetStatus("Auditioning (live)...");
    }

    private void StopAudition()
    {
        if (_audition == null)
            return;

        try { BlueSky.Editor.Program._echo?.StopSound(_audition); }
        catch { /* best effort */ }

        _audition = null;
    }

    private void CloseDocument(int index)
    {
        if (index < 0 || index >= _documents.Count)
            return;

        var doc = _documents[index];
        if (doc.Dirty)
            SaveDocument(doc);

        if (_audition != null)
            StopAudition();

        _documents.RemoveAt(index);
        _activeTab = Math.Clamp(_activeTab, 0, Math.Max(0, _documents.Count - 1));

        if (_documents.Count == 0)
        {
            _open = false;
            _window.Hide();
        }
    }

    private void RenderWindow()
    {
        float fbW = _window.FramebufferSize.X;
        float fbH = _window.FramebufferSize.Y;
        if (fbW < 2 || fbH < 2)
            return;

        EnsureDepth((uint)fbW, (uint)fbH);

        _swapchain.AcquireNextImage();
        var cmd = _device.CreateCommandBuffer();
        try
        {
            cmd.BeginRenderPass(
                new[] { _swapchain.CurrentRenderTarget },
                _depth,
                new ClearValue { Color = EditorTheme.Bg0, Depth = 1.0f });

            cmd.SetViewport(new Viewport { X = 0, Y = 0, Width = (uint)fbW, Height = (uint)fbH, MinDepth = 0, MaxDepth = 1 });
            cmd.SetScissor(new Scissor { X = 0, Y = 0, Width = (uint)fbW, Height = (uint)fbH });
            _renderer.Render(cmd, _ui);
            cmd.EndRenderPass();
            _device.Submit(cmd, _swapchain);
            _swapchain.Present();
        }
        finally
        {
            cmd.Dispose();
        }
    }

    private void EnsureDepth(uint width, uint height)
    {
        if (_depth != null && _depthW == width && _depthH == height)
            return;

        _depth?.Dispose();
        _depth = _device.CreateTexture(new TextureDesc
        {
            Width = width,
            Height = height,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil | TextureUsage.Sampled,
            DebugName = "EchoWindowDepth"
        });
        _depthW = width;
        _depthH = height;
    }

    private static string ResolveFullPath(string path)
    {
        try
        {
            string resolved = BlueSky.Core.Assets.AssetPathResolver.Resolve(path);
            return string.IsNullOrWhiteSpace(resolved) ? Path.GetFullPath(path) : Path.GetFullPath(resolved);
        }
        catch
        {
            return path;
        }
    }

    private static string ToProjectRelative(string fullPath)
    {
        string? projectDir = BlueSky.Editor.ProjectManager.CurrentProjectDir;
        if (string.IsNullOrEmpty(projectDir))
            return fullPath;

        try
        {
            string relative = Path.GetRelativePath(projectDir, fullPath).Replace('\\', '/');
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                return relative;
        }
        catch
        {
            // Fall through.
        }

        return fullPath;
    }

    private static string EffectShortName(EchoEffectType type) => type switch
    {
        EchoEffectType.Gain => "Gain",
        EchoEffectType.Equalizer => "EQ",
        EchoEffectType.LowPass => "Low-Pass",
        EchoEffectType.HighPass => "High-Pass",
        EchoEffectType.Delay => "Delay",
        EchoEffectType.Reverb => "Reverb",
        EchoEffectType.Compressor => "Compressor",
        EchoEffectType.Distortion => "Distortion",
        _ => type.ToString()
    };

    public void Dispose()
    {
        StopAudition();
        _depth?.Dispose();
        _swapchain.Dispose();
        _renderer.Dispose();
        _window.Dispose();
    }
}
