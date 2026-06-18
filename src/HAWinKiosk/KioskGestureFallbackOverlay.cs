using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HAWinKiosk.Mqtt.Models;

namespace HAWinKiosk;

/// <summary>
/// Transparent WPF overlay that detects kiosk gestures when WebView script injection fails (e.g. error pages).
/// Mirrors tap/swipe logic from <see cref="WebViewBridge"/>.
/// </summary>
public sealed class KioskGestureFallbackOverlay : Border
{
    private GesturesConfig _gestures = new();
    private Action<string>? _onGesture;
    private readonly DispatcherTimer _tapTimer = new();

    private int _tapCount;
    private long _lastTapAt;
    private double _tapX;
    private double _tapY;

    private bool _active;
    private double _sx;
    private double _sy;
    private long _st;
    private double _maxDx;
    private double _maxDy;
    private bool _holdTriggered;
    private DispatcherTimer? _holdTimer;

    private long _lastGestureAt;

    public KioskGestureFallbackOverlay()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        IsHitTestVisible = false;
        _tapTimer.Interval = TimeSpan.FromMilliseconds(360);
        _tapTimer.Tick += (_, _) => EvaluateTapBurst();

        PreviewMouseLeftButtonDown += OnPointerDown;
        PreviewMouseMove += OnPointerMove;
        PreviewMouseLeftButtonUp += OnPointerUp;
        PreviewTouchDown += OnTouchDown;
        PreviewTouchMove += OnTouchMove;
        PreviewTouchUp += OnTouchUp;
    }

    public void Configure(GesturesConfig gestures, Action<string> onGesture)
    {
        _gestures = gestures;
        _onGesture = onGesture;
    }

    public void SetFallbackActive(bool active) => IsHitTestVisible = active;

    private static bool GestureActionEnabled(string? action) =>
        !string.Equals(action, "disabled", StringComparison.OrdinalIgnoreCase);

    private bool InTapLocation(string loc, double x, double y)
    {
        if (string.Equals(loc, "anywhere", StringComparison.OrdinalIgnoreCase)) return true;
        const double m = 64;
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return true;
        return loc.ToLowerInvariant() switch
        {
            "top-right" => x >= w - m && y <= m,
            "bottom-left" => x <= m && y >= h - m,
            "bottom-right" => x >= w - m && y >= h - m,
            _ => x <= m && y <= m
        };
    }

    private void TriggerGesture(string name)
    {
        var now = Environment.TickCount64;
        if (now - _lastGestureAt < 250) return;
        _lastGestureAt = now;
        _onGesture?.Invoke(name);
    }

    private void EvaluateTapBurst()
    {
        _tapTimer.Stop();
        var c = _tapCount;
        var x = _tapX;
        var y = _tapY;
        _tapCount = 0;

        var g = _gestures;
        (bool enabled, int count, string key, string loc)[] candidates =
        [
            (GestureActionEnabled(g.QuintupleTapAction), 5, "quintupleTap", g.QuintupleTapLocation ?? "top-left"),
            (GestureActionEnabled(g.QuadrupleTapAction), 4, "quadrupleTap", g.QuadrupleTapLocation ?? "top-left"),
            (GestureActionEnabled(g.TripleTapAction), 3, "tripleTap", g.TripleTapLocation ?? "top-left"),
            (GestureActionEnabled(g.DoubleTapAction), 2, "doubleTap", g.DoubleTapLocation ?? "top-left"),
            (GestureActionEnabled(g.SingleTapAction), 1, "singleTap", g.SingleTapLocation ?? "top-left")
        ];

        foreach (var (enabled, count, key, loc) in candidates)
        {
            if (!enabled || c < count || !InTapLocation(loc, x, y)) continue;
            TriggerGesture(key);
            return;
        }
    }

    private void HandleTap(double x, double y)
    {
        var now = Environment.TickCount64;
        if (now - _lastTapAt > 1200) _tapCount = 0;
        _lastTapAt = now;
        _tapCount++;
        _tapX = x;
        _tapY = y;
        _tapTimer.Stop();
        _tapTimer.Start();
    }

    private static bool SwipeMatches(double dx, double dy, string dir)
    {
        const double m = 80;
        const double dominance = 1.15;
        var ax = Math.Abs(dx);
        var ay = Math.Abs(dy);
        return dir.ToLowerInvariant() switch
        {
            "up" => dy < -m && ay > ax * dominance,
            "right" => dx > m && ax > ay * dominance,
            "left" => dx < -m && ax > ay * dominance,
            _ => dy > m && ay > ax * dominance
        };
    }

    private bool DoSwipe(double dx, double dy, long dt, double endX, double endY)
    {
        var swipeEnabled = GestureActionEnabled(_gestures.SwipeAction);
        var swipeHoldEnabled = GestureActionEnabled(_gestures.SwipeHoldAction);
        if (!swipeEnabled && !swipeHoldEnabled) return false;

        var holdMs = (int)Math.Max(100, _gestures.SwipeHoldMs);
        if (dt >= holdMs)
        {
            if (!swipeHoldEnabled) return false;
            if (!SwipeMatches(_maxDx, _maxDy, _gestures.SwipeHoldDirection ?? "down")) return false;
            TriggerGesture("swipeHold");
            return true;
        }

        if (!swipeEnabled) return false;
        if (!SwipeMatches(dx, dy, _gestures.SwipeDirection ?? "down")) return false;
        TriggerGesture("swipe");
        return true;
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsHitTestVisible) return;
        var p = e.GetPosition(this);
        _sx = p.X;
        _sy = p.Y;
        _st = Environment.TickCount64;
        _active = true;
        _maxDx = 0;
        _maxDy = 0;
        _holdTriggered = false;
        _holdTimer?.Stop();
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(100, _gestures.SwipeHoldMs)) };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer?.Stop();
            if (!_active || _holdTriggered || !GestureActionEnabled(_gestures.SwipeHoldAction)) return;
            if (!SwipeMatches(_maxDx, _maxDy, _gestures.SwipeHoldDirection ?? "down")) return;
            _holdTriggered = true;
            TriggerGesture("swipeHold");
        };
        _holdTimer.Start();
    }

    private void OnPointerMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_active) return;
        var p = e.GetPosition(this);
        var dx = p.X - _sx;
        var dy = p.Y - _sy;
        if (dx * dx + dy * dy > _maxDx * _maxDx + _maxDy * _maxDy)
        {
            _maxDx = dx;
            _maxDy = dy;
        }
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!_active) return;
        _active = false;
        _holdTimer?.Stop();
        _holdTimer = null;
        if (_holdTriggered) return;

        var p = e.GetPosition(this);
        var dx = p.X - _sx;
        var dy = p.Y - _sy;
        var dt = Environment.TickCount64 - _st;
        if (DoSwipe(dx, dy, dt, p.X, p.Y)) return;
        if (Math.Abs(dx) > 20 || Math.Abs(dy) > 20 || dt > 450) return;
        HandleTap(p.X, p.Y);
    }

    private void OnTouchDown(object? sender, TouchEventArgs e)
    {
        if (!IsHitTestVisible) return;
        CaptureTouch(e.TouchDevice);
        var p = e.GetTouchPoint(this).Position;
        _sx = p.X;
        _sy = p.Y;
        _st = Environment.TickCount64;
        _active = true;
        _maxDx = 0;
        _maxDy = 0;
        _holdTriggered = false;
        _holdTimer?.Stop();
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(100, _gestures.SwipeHoldMs)) };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer?.Stop();
            if (!_active || _holdTriggered || !GestureActionEnabled(_gestures.SwipeHoldAction)) return;
            if (!SwipeMatches(_maxDx, _maxDy, _gestures.SwipeHoldDirection ?? "down")) return;
            _holdTriggered = true;
            TriggerGesture("swipeHold");
        };
        _holdTimer.Start();
    }

    private void OnTouchMove(object? sender, TouchEventArgs e)
    {
        if (!_active) return;
        var p = e.GetTouchPoint(this).Position;
        var dx = p.X - _sx;
        var dy = p.Y - _sy;
        if (dx * dx + dy * dy > _maxDx * _maxDx + _maxDy * _maxDy)
        {
            _maxDx = dx;
            _maxDy = dy;
        }
    }

    private void OnTouchUp(object? sender, TouchEventArgs e)
    {
        if (!_active) return;
        _active = false;
        ReleaseTouchCapture(e.TouchDevice);
        _holdTimer?.Stop();
        _holdTimer = null;
        if (_holdTriggered) return;

        var p = e.GetTouchPoint(this).Position;
        var dx = p.X - _sx;
        var dy = p.Y - _sy;
        var dt = Environment.TickCount64 - _st;
        if (DoSwipe(dx, dy, dt, p.X, p.Y)) return;
        if (Math.Abs(dx) > 20 || Math.Abs(dy) > 20 || dt > 450) return;
        HandleTap(p.X, p.Y);
    }
}
