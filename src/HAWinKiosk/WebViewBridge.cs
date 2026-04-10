using System.Text;
using System.Text.Json;
using HAWinKiosk.Mqtt.Models;

namespace HAWinKiosk;

/// <summary>Injects gesture handlers into a document or iframe window.</summary>
public static class WebViewBridge
{
    public static string BuildDocumentScript(KioskConfig kiosk)
    {
        var g = kiosk.Gestures;
        var dto = new
        {
            doubleTapEnabled = !string.Equals(g.DoubleTapAction, "disabled", StringComparison.OrdinalIgnoreCase),
            swipeEnabled = !string.Equals(g.SwipeAction, "disabled", StringComparison.OrdinalIgnoreCase),
            twoFingerSwipeEnabled = !string.Equals(g.TwoFingerSwipeAction, "disabled", StringComparison.OrdinalIgnoreCase),
            swipeHoldEnabled = !string.Equals(g.SwipeHoldAction, "disabled", StringComparison.OrdinalIgnoreCase),
            twoFingerSwipeHoldEnabled = !string.Equals(g.TwoFingerSwipeHoldAction, "disabled", StringComparison.OrdinalIgnoreCase),
            zoomEnabled = !string.Equals(g.ZoomAction, "disabled", StringComparison.OrdinalIgnoreCase),
            pinchEnabled = !string.Equals(g.PinchAction, "disabled", StringComparison.OrdinalIgnoreCase),
            tripleTapEnabled = !string.Equals(g.TripleTapAction, "disabled", StringComparison.OrdinalIgnoreCase),
            quadTapEnabled = !string.Equals(g.QuadrupleTapAction, "disabled", StringComparison.OrdinalIgnoreCase),
            quintTapEnabled = !string.Equals(g.QuintupleTapAction, "disabled", StringComparison.OrdinalIgnoreCase),
            doubleTapAction = (g.DoubleTapAction ?? "disabled").ToLowerInvariant(),
            swipeAction = (g.SwipeAction ?? "disabled").ToLowerInvariant(),
            twoFingerSwipeAction = (g.TwoFingerSwipeAction ?? "disabled").ToLowerInvariant(),
            swipeHoldAction = (g.SwipeHoldAction ?? "disabled").ToLowerInvariant(),
            twoFingerSwipeHoldAction = (g.TwoFingerSwipeHoldAction ?? "disabled").ToLowerInvariant(),
            zoomAction = (g.ZoomAction ?? "disabled").ToLowerInvariant(),
            pinchAction = (g.PinchAction ?? "disabled").ToLowerInvariant(),
            tripleTapAction = (g.TripleTapAction ?? "disabled").ToLowerInvariant(),
            quadTapAction = (g.QuadrupleTapAction ?? "disabled").ToLowerInvariant(),
            quintTapAction = (g.QuintupleTapAction ?? "disabled").ToLowerInvariant(),
            doubleTapLocation = (g.DoubleTapLocation ?? "top-left").ToLowerInvariant(),
            tripleTapLocation = (g.TripleTapLocation ?? "top-left").ToLowerInvariant(),
            quadTapLocation = (g.QuadrupleTapLocation ?? "top-left").ToLowerInvariant(),
            quintTapLocation = (g.QuintupleTapLocation ?? "top-left").ToLowerInvariant(),
            swipeDir = (g.SwipeDirection ?? "down").ToLowerInvariant(),
            twoFingerSwipeDir = (g.TwoFingerSwipeDirection ?? "down").ToLowerInvariant(),
            swipeHoldDir = (g.SwipeHoldDirection ?? "down").ToLowerInvariant(),
            twoFingerSwipeHoldDir = (g.TwoFingerSwipeHoldDirection ?? "down").ToLowerInvariant(),
            swipeHoldMs = (int)Math.Max(100, g.SwipeHoldMs),
            twoFingerSwipeHoldMs = (int)Math.Max(100, g.TwoFingerSwipeHoldMs),
            zoomDirection = (g.ZoomDirection ?? "any").ToLowerInvariant(),
            minSwipePx = Math.Max(20, g.MinSwipePixels),
            // Matches kiosk chrome: white tick in light mode, near-black tick + cyan glow in dark mode (see theme mockups).
            gestureTickDark = UiThemeHelper.ResolveEffectiveDark(kiosk.UiTheme)
        };

        var json = JsonSerializer.Serialize(dto);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        return $$"""
(function(){
  const cfg = JSON.parse(atob('{{b64}}'));
  let tapCount = 0, lastTapAt = 0, tapTimer = null, tapX = 0, tapY = 0;
  let sx = 0, sy = 0, st = 0, active = false;
  let maxDx = 0, maxDy = 0, maxD2 = 0;
  let pinchStartDist = 0, pinchActive = false, pinchFired = false, pinchCx = 0, pinchCy = 0;
  let twoFx = 0, twoFy = 0, twoFst = 0, twoFActive = false, twoFMaxDx = 0, twoFMaxDy = 0, twoFHoldTriggered = false, twoFHoldTimer = null;
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
    const outer = document.createElement('div');
    outer.style.position = 'fixed';
    outer.style.left = Math.max(24, Math.min(window.innerWidth - 24, x)) + 'px';
    outer.style.top = Math.max(24, Math.min(window.innerHeight - 24, y)) + 'px';
    outer.style.width = '100px';
    outer.style.height = '100px';
    outer.style.marginLeft = '-50px';
    outer.style.marginTop = '-50px';
    outer.style.zIndex = '2147483647';
    outer.style.pointerEvents = 'none';
    outer.style.display = 'flex';
    outer.style.alignItems = 'center';
    outer.style.justifyContent = 'center';
    outer.style.opacity = '0';
    outer.style.transform = 'scale(.6)';
    outer.style.transition = 'transform 220ms cubic-bezier(.2,.9,.2,1), opacity 260ms ease';

    const glow = document.createElement('div');
    glow.style.position = 'absolute';
    glow.style.left = '0';
    glow.style.top = '0';
    glow.style.width = '100%';
    glow.style.height = '100%';
    glow.style.borderRadius = '50%';
    glow.style.pointerEvents = 'none';
    glow.style.background = 'radial-gradient(circle closest-side, rgba(22,185,240,0.58) 0%, rgba(22,185,240,0.28) 38%, rgba(22,185,240,0.09) 58%, transparent 76%)';

    const svgNs = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(svgNs, 'svg');
    svg.setAttribute('width', '52');
    svg.setAttribute('height', '52');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.style.position = 'relative';
    svg.style.zIndex = '1';
    svg.style.filter = cfg.gestureTickDark ? 'none' : 'drop-shadow(0 1px 2px rgba(0,0,0,0.12))';
    const path = document.createElementNS(svgNs, 'path');
    path.setAttribute('fill', 'none');
    path.setAttribute('stroke', cfg.gestureTickDark ? '#141414' : '#ffffff');
    path.setAttribute('stroke-width', '3');
    path.setAttribute('stroke-linecap', 'round');
    path.setAttribute('stroke-linejoin', 'round');
    path.setAttribute('d', 'M5.5 12.5l4 4L18.5 7');
    svg.appendChild(path);

    outer.appendChild(glow);
    outer.appendChild(svg);
    const root = document.body || document.documentElement;
    root.appendChild(outer);
    outer.getBoundingClientRect();
    requestAnimationFrame(() => {
      outer.style.opacity = '1';
      outer.style.transform = 'scale(1.08)';
      requestAnimationFrame(() => { outer.style.transform = 'scale(1)'; });
    });
    setTimeout(() => {
      outer.style.opacity = '0';
      outer.style.transform = 'scale(.86)';
      setTimeout(() => outer.remove(), 280);
    }, 760);
  }
  function actionForGesture(name) {
    switch ((name || '').toLowerCase()) {
      case 'doubletap': return cfg.doubleTapAction || 'disabled';
      case 'swipe': return cfg.swipeAction || 'disabled';
      case 'twofingerswipe': return cfg.twoFingerSwipeAction || 'disabled';
      case 'swipehold': return cfg.swipeHoldAction || 'disabled';
      case 'twofingerswipehold': return cfg.twoFingerSwipeHoldAction || 'disabled';
      case 'zoom': return cfg.zoomAction || 'disabled';
      case 'pinch': return cfg.pinchAction || 'disabled';
      case 'tripletap': return cfg.tripleTapAction || 'disabled';
      case 'quadrupletap': return cfg.quadTapAction || 'disabled';
      case 'quintupletap': return cfg.quintTapAction || 'disabled';
      default: return 'disabled';
    }
  }
  function triggerGesture(name, x, y) {
    const now = Date.now();
    if (now - lastGestureAt < 250) return;
    lastGestureAt = now;
    if (actionForGesture(name) !== 'settings') {
      showAck(x, y);
    }
    // Give touch devices enough time to paint ack before disruptive actions (reload/settings).
    setTimeout(() => post({ type: 'gesture', gesture: name }), 220);
  }
  function evaluateTapBurst() {
    const c = tapCount;
    const x = tapX;
    const y = tapY;
    tapCount = 0;
    tapTimer = null;

    const candidates = [
      { enabled: cfg.quintTapEnabled, count: 5, key: 'quintupleTap', loc: cfg.quintTapLocation },
      { enabled: cfg.quadTapEnabled, count: 4, key: 'quadrupleTap', loc: cfg.quadTapLocation },
      { enabled: cfg.tripleTapEnabled, count: 3, key: 'tripleTap', loc: cfg.tripleTapLocation },
      { enabled: cfg.doubleTapEnabled, count: 2, key: 'doubleTap', loc: cfg.doubleTapLocation }
    ];

    for (const g of candidates) {
      if (!g.enabled) continue;
      if (c < g.count) continue;
      if (!inTapLocation(g.loc, x, y)) continue;
      triggerGesture(g.key, x, y);
      return;
    }
  }

  function handleTap(clientX, clientY) {
    const now = Date.now();
    if (now - lastTapAt > 1200) tapCount = 0;
    lastTapAt = now;
    tapCount++;
    tapX = clientX;
    tapY = clientY;
    if (tapTimer) clearTimeout(tapTimer);
    tapTimer = setTimeout(evaluateTapBurst, 360);
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

  function doTwoFingerSwipe(dx, dy, dt, endX, endY) {
    if (!(cfg.twoFingerSwipeEnabled || cfg.twoFingerSwipeHoldEnabled)) return false;
    if (dt >= cfg.twoFingerSwipeHoldMs) {
      if (!cfg.twoFingerSwipeHoldEnabled) return false;
      if (!swipeMatches(twoFMaxDx, twoFMaxDy, cfg.twoFingerSwipeHoldDir)) return false;
      triggerGesture('twoFingerSwipeHold', endX, endY);
      return true;
    }
    if (!cfg.twoFingerSwipeEnabled) return false;
    if (!swipeMatches(dx, dy, cfg.twoFingerSwipeDir)) return false;
    triggerGesture('twoFingerSwipe', endX, endY);
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
    if (e.touches.length !== 2) return;
    if (!(cfg.pinchEnabled || cfg.zoomEnabled || cfg.twoFingerSwipeEnabled || cfg.twoFingerSwipeHoldEnabled)) return;
    pinchStartDist = dist(e.touches[0], e.touches[1]);
    pinchCx = (e.touches[0].clientX + e.touches[1].clientX) / 2;
    pinchCy = (e.touches[0].clientY + e.touches[1].clientY) / 2;
    pinchActive = cfg.pinchEnabled || cfg.zoomEnabled;
    pinchFired = false;
    twoFActive = true;
    twoFx = pinchCx;
    twoFy = pinchCy;
    twoFst = Date.now();
    twoFMaxDx = 0;
    twoFMaxDy = 0;
    twoFHoldTriggered = false;
    if (twoFHoldTimer) { clearTimeout(twoFHoldTimer); twoFHoldTimer = null; }
    twoFHoldTimer = setTimeout(() => {
      if (!twoFActive || twoFHoldTriggered || !cfg.twoFingerSwipeHoldEnabled) return;
      const endX = twoFx + twoFMaxDx, endY = twoFy + twoFMaxDy;
      if (!swipeMatches(twoFMaxDx, twoFMaxDy, cfg.twoFingerSwipeHoldDir)) return;
      twoFHoldTriggered = true;
      triggerGesture('twoFingerSwipeHold', endX, endY);
    }, cfg.twoFingerSwipeHoldMs);
  }

  function onTouchMove(e) {
    if (edgeSwipeTracking) {
      e.preventDefault();
    }
    if (e.touches.length !== 2) return;
    const current = dist(e.touches[0], e.touches[1]);
    pinchCx = (e.touches[0].clientX + e.touches[1].clientX) / 2;
    pinchCy = (e.touches[0].clientY + e.touches[1].clientY) / 2;
    const delta = current - pinchStartDist;
    if (pinchActive && !pinchFired && Math.abs(delta) >= 40) {
      pinchFired = true;
      if (delta < 0 && cfg.pinchEnabled) {
        // Fingers moving together (pinch-in).
        triggerGesture('pinch', pinchCx, pinchCy);
      } else if (delta > 0 && cfg.zoomEnabled) {
        // Fingers moving apart (zoom-out).
        const zoomDir = 'out';
        if (cfg.zoomDirection === 'any' || cfg.zoomDirection === zoomDir) {
          triggerGesture('zoom', pinchCx, pinchCy);
        }
      }
    }

    if (twoFActive) {
      const dx = pinchCx - twoFx, dy = pinchCy - twoFy;
      if ((dx * dx + dy * dy) > (twoFMaxDx * twoFMaxDx + twoFMaxDy * twoFMaxDy)) {
        twoFMaxDx = dx;
        twoFMaxDy = dy;
      }
      if (!twoFHoldTriggered && cfg.twoFingerSwipeHoldEnabled && (Date.now() - twoFst) >= cfg.twoFingerSwipeHoldMs && swipeMatches(twoFMaxDx, twoFMaxDy, cfg.twoFingerSwipeHoldDir)) {
        twoFHoldTriggered = true;
        triggerGesture('twoFingerSwipeHold', pinchCx, pinchCy);
      }
    }
  }

  function onTouchEnd(e) {
    const hadTwoF = twoFActive;
    const endX = pinchCx;
    const endY = pinchCy;
    const dx = twoFMaxDx;
    const dy = twoFMaxDy;
    const dt = Date.now() - twoFst;
    pinchActive = false;
    edgeSwipeTracking = false;
    twoFActive = false;
    if (twoFHoldTimer) { clearTimeout(twoFHoldTimer); twoFHoldTimer = null; }
    if (hadTwoF && !twoFHoldTriggered && e.touches.length < 2) {
      doTwoFingerSwipe(dx, dy, dt, endX, endY);
    }
    twoFHoldTriggered = false;
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
    if (twoFHoldTimer) { clearTimeout(twoFHoldTimer); twoFHoldTimer = null; }
    if (tapTimer) { clearTimeout(tapTimer); tapTimer = null; }
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
