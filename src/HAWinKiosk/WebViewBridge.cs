using System.Text;
using System.Text.Json;
using HAWinKiosk.Mqtt.Models;

namespace HAWinKiosk;

/// <summary>Injects gesture handlers (swipe/swipe-hold/pinch/triple-tap/quadruple-tap) into a document or iframe window.</summary>
public static class WebViewBridge
{
    public static string BuildDocumentScript(KioskConfig kiosk)
    {
        var g = kiosk.Gestures;
        var dto = new
        {
            swipeEnabled = !string.Equals(g.SwipeAction, "disabled", StringComparison.OrdinalIgnoreCase),
            swipeHoldEnabled = !string.Equals(g.SwipeHoldAction, "disabled", StringComparison.OrdinalIgnoreCase),
            pinchEnabled = !string.Equals(g.PinchAction, "disabled", StringComparison.OrdinalIgnoreCase),
            tripleTapEnabled = !string.Equals(g.TripleTapAction, "disabled", StringComparison.OrdinalIgnoreCase),
            quadTapEnabled = !string.Equals(g.QuadrupleTapAction, "disabled", StringComparison.OrdinalIgnoreCase),
            tripleTapLocation = (g.TripleTapLocation ?? "top-left").ToLowerInvariant(),
            quadTapLocation = (g.QuadrupleTapLocation ?? "top-left").ToLowerInvariant(),
            swipeDir = (g.SwipeDirection ?? "down").ToLowerInvariant(),
            swipeHoldDir = (g.SwipeHoldDirection ?? "down").ToLowerInvariant(),
            swipeHoldMs = (int)Math.Max(100, g.SwipeHoldMs),
            minSwipePx = Math.Max(20, g.MinSwipePixels)
        };

        var json = JsonSerializer.Serialize(dto);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        return $$"""
(function(){
  const cfg = JSON.parse(atob('{{b64}}'));
  let tripleTapCount = 0, lastTripleTap = 0, tripleTimer = null;
  let quadTapCount = 0, lastQuadTap = 0;
  let sx = 0, sy = 0, st = 0, active = false;
  let maxDx = 0, maxDy = 0, maxD2 = 0;
  let pinchStartDist = 0, pinchActive = false, pinchFired = false, pinchCx = 0, pinchCy = 0;
  let lastGestureAt = 0;
  let holdTriggered = false;
  let holdTimer = null;
  let priorCursor = '';
  let edgeSwipeTracking = false;

  if (typeof window.__haWinKioskGestureCleanup === 'function') {
    try { window.__haWinKioskGestureCleanup(); } catch (e) {}
  }

  function inTapLocation(loc, x, y) {
    if (loc === 'anywhere') return true;
    const w = window.innerWidth, h = window.innerHeight, m = 64;
    switch (loc) {
      case 'top-right': return x >= w - m && y <= m;
      case 'bottom-left': return x <= m && y >= h - m;
      case 'bottom-right': return x >= w - m && y >= h - m;
      default: return x <= m && y <= m;
    }
  }
  function post(o) {
    try { chrome.webview.postMessage(o); } catch (e) {}
  }
  function blockZoomAndBrowserSwipe() {
    try {
      const root = document.documentElement;
      if (root) {
        root.style.overscrollBehaviorX = 'none';
        root.style.overscrollBehaviorY = 'none';
        root.style.touchAction = 'manipulation';
      }
      if (document.body) {
        document.body.style.overscrollBehaviorX = 'none';
        document.body.style.overscrollBehaviorY = 'none';
      }
    } catch (e) {}
  }
  function showAck(x, y) {
    if (!Number.isFinite(x) || !Number.isFinite(y)) return;
    const d = document.createElement('div');
    d.style.position = 'fixed';
    d.style.left = Math.max(24, Math.min(window.innerWidth - 24, x)) + 'px';
    d.style.top = Math.max(24, Math.min(window.innerHeight - 24, y)) + 'px';
    d.style.width = '68px';
    d.style.height = '68px';
    d.style.marginLeft = '-34px';
    d.style.marginTop = '-34px';
    d.style.borderRadius = '9999px';
    d.style.background = '#16B9F0';
    d.style.boxShadow = '0 0 0 8px rgba(22,185,240,.28), 0 0 0 16px rgba(22,185,240,.18)';
    d.style.zIndex = '2147483647';
    d.style.pointerEvents = 'none';
    d.style.display = 'grid';
    d.style.placeItems = 'center';
    d.style.opacity = '0';
    d.style.transform = 'scale(.6)';
    d.style.transition = 'transform 220ms cubic-bezier(.2,.9,.2,1), opacity 260ms ease';
    d.style.color = 'white';
    d.style.fontFamily = 'Segoe UI, sans-serif';
    d.style.fontSize = '38px';
    d.style.fontWeight = '700';
    d.style.lineHeight = '1';
    d.style.textAlign = 'center';
    d.textContent = '✓';
    const root = document.body || document.documentElement;
    root.appendChild(d);
    // Force layout so the element is definitely committed before action dispatch.
    d.getBoundingClientRect();
    requestAnimationFrame(() => {
      d.style.opacity = '1';
      d.style.transform = 'scale(1.08)';
      requestAnimationFrame(() => { d.style.transform = 'scale(1)'; });
    });
    setTimeout(() => {
      d.style.opacity = '0';
      d.style.transform = 'scale(.86)';
      setTimeout(() => d.remove(), 280);
    }, 760);
  }
  function triggerGesture(name, x, y) {
    const now = Date.now();
    if (now - lastGestureAt < 250) return;
    lastGestureAt = now;
    showAck(x, y);
    // Give touch devices enough time to paint ack before disruptive actions (reload/settings).
    setTimeout(() => post({ type: 'gesture', gesture: name }), 220);
  }
  function handleTap(clientX, clientY) {
    const tripleLocationMatch = cfg.tripleTapEnabled && inTapLocation(cfg.tripleTapLocation, clientX, clientY);
    const quadLocationMatch = cfg.quadTapEnabled && inTapLocation(cfg.quadTapLocation, clientX, clientY);
    if (!tripleLocationMatch && !quadLocationMatch) return;
    const now = Date.now();
    const sameTapLocation = cfg.tripleTapLocation === cfg.quadTapLocation;

    if (quadLocationMatch) {
      if (now - lastQuadTap > 1200) quadTapCount = 0;
      lastQuadTap = now;
      quadTapCount++;
    }

    if (tripleLocationMatch) {
      if (now - lastTripleTap > 1200) {
        tripleTapCount = 0;
        if (tripleTimer) { clearTimeout(tripleTimer); tripleTimer = null; }
      }
      lastTripleTap = now;
      tripleTapCount++;
    }

    if (cfg.quadTapEnabled && quadLocationMatch && quadTapCount >= 4) {
      if (tripleTimer) { clearTimeout(tripleTimer); tripleTimer = null; }
      triggerGesture('quadrupleTap', clientX, clientY);
      quadTapCount = 0;
      if (sameTapLocation) tripleTapCount = 0;
      return;
    }

    if (cfg.tripleTapEnabled && tripleLocationMatch && tripleTapCount >= 3) {
      if (cfg.quadTapEnabled && sameTapLocation) {
        if (tripleTimer) clearTimeout(tripleTimer);
        tripleTimer = setTimeout(() => {
          if (tripleTapCount === 3) {
            triggerGesture('tripleTap', clientX, clientY);
            tripleTapCount = 0;
          }
          tripleTimer = null;
        }, 350);
      } else {
        triggerGesture('tripleTap', clientX, clientY);
        tripleTapCount = 0;
      }
    }
  }

  function swipeMatches(dx, dy, dir) {
    const m = cfg.minSwipePx, ax = Math.abs(dx), ay = Math.abs(dy);
    const dominance = 1.15;
    switch (dir) {
      case 'up': return dy < -m && ay > ax * dominance;
      case 'right': return dx > m && ax > ay * dominance;
      case 'left': return dx < -m && ax > ay * dominance;
      case 'down':
      default: return dy > m && ay > ax * dominance;
    }
  }
  function doSwipe(dx, dy, dt, endX, endY) {
    if (!(cfg.swipeEnabled || cfg.swipeHoldEnabled)) return false;
    if (dt >= cfg.swipeHoldMs) {
      if (!cfg.swipeHoldEnabled) return false;
      if (!swipeMatches(maxDx, maxDy, cfg.swipeHoldDir)) return false;
      triggerGesture('swipeHold', endX, endY);
      return true;
    }
    if (!cfg.swipeEnabled) return false;
    if (!swipeMatches(dx, dy, cfg.swipeDir)) return false;
    triggerGesture('swipe', endX, endY);
    return true;
  }

  function onPointerDown(e) {
    if (!e.isPrimary) return;
    if (e.pointerType === 'mouse' && e.button !== 0) return;
    sx = e.clientX; sy = e.clientY; st = Date.now(); active = true;
    maxDx = 0; maxDy = 0; maxD2 = 0;
    holdTriggered = false;
    if (holdTimer) { clearTimeout(holdTimer); holdTimer = null; }
    priorCursor = document.documentElement.style.cursor || '';
    document.documentElement.style.cursor = 'grabbing';
    holdTimer = setTimeout(() => {
      if (!active || holdTriggered || !cfg.swipeHoldEnabled) return;
      const endX = sx + maxDx, endY = sy + maxDy;
      if (!swipeMatches(maxDx, maxDy, cfg.swipeHoldDir)) return;
      holdTriggered = true;
      triggerGesture('swipeHold', endX, endY);
    }, cfg.swipeHoldMs);
  }

  function onPointerMove(e) {
    if (!active) return;
    const dx = e.clientX - sx, dy = e.clientY - sy;
    const d2 = (dx * dx) + (dy * dy);
    if (d2 > maxD2) {
      maxD2 = d2;
      maxDx = dx;
      maxDy = dy;
    }
    if (!holdTriggered && cfg.swipeHoldEnabled && (Date.now() - st) >= cfg.swipeHoldMs && swipeMatches(maxDx, maxDy, cfg.swipeHoldDir)) {
      holdTriggered = true;
      triggerGesture('swipeHold', e.clientX, e.clientY);
    }
  }
  function onPointerUp(e) {
    if (!active) return;
    active = false;
    if (holdTimer) { clearTimeout(holdTimer); holdTimer = null; }
    document.documentElement.style.cursor = priorCursor;
    if (holdTriggered) return;
    const dx = e.clientX - sx, dy = e.clientY - sy;
    const dt = Date.now() - st;
    if (doSwipe(dx, dy, dt, e.clientX, e.clientY)) return;
    if (Math.abs(dx) > 20 || Math.abs(dy) > 20 || dt > 450) return;
    handleTap(e.clientX, e.clientY);
  }
  function onPointerCancel() {
    if (!active) return;
    active = false;
    if (holdTimer) { clearTimeout(holdTimer); holdTimer = null; }
    document.documentElement.style.cursor = priorCursor;
    if (holdTriggered) return;
    const dt = Date.now() - st;
    const endX = sx + maxDx, endY = sy + maxDy;
    doSwipe(maxDx, maxDy, dt, endX, endY);
  }

  function dist(a, b) {
    const dx = a.clientX - b.clientX;
    const dy = a.clientY - b.clientY;
    return Math.hypot(dx, dy);
  }

  function onTouchStart(e) {
    if (e.touches.length === 1) {
      const x = e.touches[0].clientX;
      const edge = 24;
      edgeSwipeTracking = x <= edge || x >= (window.innerWidth - edge);
    } else {
      edgeSwipeTracking = false;
    }
    if (!cfg.pinchEnabled || e.touches.length !== 2) return;
    pinchStartDist = dist(e.touches[0], e.touches[1]);
    pinchCx = (e.touches[0].clientX + e.touches[1].clientX) / 2;
    pinchCy = (e.touches[0].clientY + e.touches[1].clientY) / 2;
    pinchActive = true;
    pinchFired = false;
  }

  function onTouchMove(e) {
    if (edgeSwipeTracking) {
      e.preventDefault();
    }
    if (!pinchActive || pinchFired || e.touches.length !== 2) return;
    const current = dist(e.touches[0], e.touches[1]);
    pinchCx = (e.touches[0].clientX + e.touches[1].clientX) / 2;
    pinchCy = (e.touches[0].clientY + e.touches[1].clientY) / 2;
    if (Math.abs(current - pinchStartDist) >= 40) {
      pinchFired = true;
      triggerGesture('pinch', pinchCx, pinchCy);
    }
  }

  function onTouchEnd() {
    pinchActive = false;
    edgeSwipeTracking = false;
  }

  function onWheel(e) {
    if (e.ctrlKey) e.preventDefault();
  }

  function onGestureEvent(e) {
    e.preventDefault();
  }

  // passive: true — do not block scrolling / default page behavior (we never call preventDefault).
  // capture: true — still see events during capture phase alongside normal interaction.
  const ptrOpts = { passive: true, capture: true };
  const activeOpts = { passive: false, capture: true };
  blockZoomAndBrowserSwipe();
  window.addEventListener('pointerdown', onPointerDown, ptrOpts);
  window.addEventListener('pointermove', onPointerMove, ptrOpts);
  window.addEventListener('pointerup', onPointerUp, ptrOpts);
  window.addEventListener('pointercancel', onPointerCancel, ptrOpts);
  window.addEventListener('touchstart', onTouchStart, activeOpts);
  window.addEventListener('touchmove', onTouchMove, activeOpts);
  window.addEventListener('touchend', onTouchEnd, activeOpts);
  window.addEventListener('touchcancel', onTouchEnd, activeOpts);
  window.addEventListener('wheel', onWheel, activeOpts);
  window.addEventListener('gesturestart', onGestureEvent, activeOpts);
  window.addEventListener('gesturechange', onGestureEvent, activeOpts);
  window.addEventListener('gestureend', onGestureEvent, activeOpts);

  window.__haWinKioskGestureCleanup = function() {
    if (holdTimer) { clearTimeout(holdTimer); holdTimer = null; }
    document.documentElement.style.cursor = priorCursor;
    window.removeEventListener('pointerdown', onPointerDown, ptrOpts);
    window.removeEventListener('pointermove', onPointerMove, ptrOpts);
    window.removeEventListener('pointerup', onPointerUp, ptrOpts);
    window.removeEventListener('pointercancel', onPointerCancel, ptrOpts);
    window.removeEventListener('touchstart', onTouchStart, activeOpts);
    window.removeEventListener('touchmove', onTouchMove, activeOpts);
    window.removeEventListener('touchend', onTouchEnd, activeOpts);
    window.removeEventListener('touchcancel', onTouchEnd, activeOpts);
    window.removeEventListener('wheel', onWheel, activeOpts);
    window.removeEventListener('gesturestart', onGestureEvent, activeOpts);
    window.removeEventListener('gesturechange', onGestureEvent, activeOpts);
    window.removeEventListener('gestureend', onGestureEvent, activeOpts);
    window.__haWinKioskGestureCleanup = null;
  };
})();
""";
    }
}
