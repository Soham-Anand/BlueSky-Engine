using System;
using System.Collections.Generic;
using System.Numerics;
using NotBSRenderer;

namespace BlueSky.Editor.UI;

/// <summary>
/// Toast notification system with smooth animations
/// </summary>
public class NotificationSystem
{
    private readonly List<Notification> _notifications = new();
    private uint _nextId = 1;
    
    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }
    
    private class Notification
    {
        public uint Id;
        public string Message;
        public string? Icon;
        public NotificationType Type;
        public float Duration;
        public float Elapsed;
        public AnimatedFloat SlideAmount;
        public AnimatedFloat FadeAmount;
        public bool IsClosing;
        
        public Notification()
        {
            SlideAmount = new AnimatedFloat(0f, 0.2f);
            FadeAmount = new AnimatedFloat(0f, 0.15f);
        }
    }
    
    public void Show(string message, NotificationType type = NotificationType.Info, float duration = 3f, string? icon = null)
    {
        var notification = new Notification
        {
            Id = _nextId++,
            Message = message,
            Icon = icon,
            Type = type,
            Duration = duration,
            Elapsed = 0f,
            IsClosing = false
        };
        
        notification.SlideAmount.SetTarget(1f);
        notification.FadeAmount.SetTarget(1f);
        
        _notifications.Add(notification);
        
        // Limit to 5 notifications
        while (_notifications.Count > 5)
        {
            _notifications.RemoveAt(0);
        }
    }
    
    public void ShowInfo(string message, float duration = 3f) => Show(message, NotificationType.Info, duration, "ℹ️");
    public void ShowSuccess(string message, float duration = 3f) => Show(message, NotificationType.Success, duration, "✓");
    public void ShowWarning(string message, float duration = 3f) => Show(message, NotificationType.Warning, duration, "⚠");
    public void ShowError(string message, float duration = 3f) => Show(message, NotificationType.Error, duration, "✗");
    
    public void Update(float deltaTime)
    {
        for (int i = _notifications.Count - 1; i >= 0; i--)
        {
            var notif = _notifications[i];
            notif.Elapsed += deltaTime;
            
            // Start closing animation near end
            if (notif.Elapsed >= notif.Duration - 0.3f && !notif.IsClosing)
            {
                notif.IsClosing = true;
                notif.SlideAmount.SetTarget(0f);
                notif.FadeAmount.SetTarget(0f);
            }
            
            // Remove when fully closed
            if (notif.Elapsed >= notif.Duration)
            {
                _notifications.RemoveAt(i);
                continue;
            }
            
            notif.SlideAmount.Update(deltaTime);
            notif.FadeAmount.Update(deltaTime);
        }
    }
    
    public void Render(NotBSUI ui, float screenWidth, float screenHeight)
    {
        float notifWidth = 320f;
        float notifHeight = 56f;
        float gap = 8f;
        float startY = screenHeight - 80f;
        float startX = screenWidth - notifWidth - 20f;
        
        for (int i = 0; i < _notifications.Count; i++)
        {
            var notif = _notifications[i];
            
            float y = startY - i * (notifHeight + gap);
            float slideOffset = (1f - notif.SlideAmount.Current) * 50f;
            float x = startX + slideOffset;
            float alpha = notif.FadeAmount.Current;
            
            if (alpha < 0.01f) continue;
            
            // Get colors based on type
            Vector4 bgColor, borderColor, iconColor;
            switch (notif.Type)
            {
                case NotificationType.Success:
                    bgColor = ModernTheme.WithAlpha(ModernTheme.Green, 0.15f * alpha);
                    borderColor = ModernTheme.WithAlpha(ModernTheme.Green, 0.6f * alpha);
                    iconColor = ModernTheme.WithAlpha(ModernTheme.Green, alpha);
                    break;
                case NotificationType.Warning:
                    bgColor = ModernTheme.WithAlpha(ModernTheme.Orange, 0.15f * alpha);
                    borderColor = ModernTheme.WithAlpha(ModernTheme.Orange, 0.6f * alpha);
                    iconColor = ModernTheme.WithAlpha(ModernTheme.Orange, alpha);
                    break;
                case NotificationType.Error:
                    bgColor = ModernTheme.WithAlpha(ModernTheme.Red, 0.15f * alpha);
                    borderColor = ModernTheme.WithAlpha(ModernTheme.Red, 0.6f * alpha);
                    iconColor = ModernTheme.WithAlpha(ModernTheme.Red, alpha);
                    break;
                default: // Info
                    bgColor = ModernTheme.WithAlpha(ModernTheme.Accent, 0.15f * alpha);
                    borderColor = ModernTheme.WithAlpha(ModernTheme.Accent, 0.6f * alpha);
                    iconColor = ModernTheme.WithAlpha(ModernTheme.Accent, alpha);
                    break;
            }
            
            // Shadow
            ui.Shadow(x + 2, y + 2, notifWidth, notifHeight, 4, 8, 0.3f * alpha);
            
            // Background
            ui.Panel(x, y, notifWidth, notifHeight, bgColor);
            
            // Border
            ui.Panel(x, y, notifWidth, 2, borderColor);
            ui.Panel(x, y + notifHeight - 2, notifWidth, 2, borderColor);
            ui.Panel(x, y, 2, notifHeight, borderColor);
            ui.Panel(x + notifWidth - 2, y, 2, notifHeight, borderColor);
            
            // Icon
            if (!string.IsNullOrEmpty(notif.Icon))
            {
                ui.SetCursor(x + 16, y + 20);
                ui.Text(notif.Icon, iconColor);
            }
            
            // Message
            float textX = x + (string.IsNullOrEmpty(notif.Icon) ? 16 : 40);
            ui.SetCursor(textX, y + 20);
            
            string displayMsg = notif.Message;
            int maxChars = (int)((notifWidth - textX + x - 16) / 7.2f);
            if (displayMsg.Length > maxChars)
                displayMsg = displayMsg[..(maxChars - 3)] + "...";
            
            ui.Text(displayMsg, ModernTheme.WithAlpha(ModernTheme.TextPrimary, alpha));
            
            // Progress bar
            float progress = notif.Elapsed / notif.Duration;
            float progressWidth = notifWidth * (1f - progress);
            ui.Panel(x, y + notifHeight - 3, progressWidth, 3, ModernTheme.WithAlpha(borderColor, 0.5f * alpha));
        }
    }
    
    public void Clear()
    {
        _notifications.Clear();
    }
}
