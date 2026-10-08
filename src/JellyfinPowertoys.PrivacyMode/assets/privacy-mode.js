(function () {
  "use strict";
  const TAP_TIMEOUT = 300;
  const PRIVACY_MODE_CLASS = "powertoysPrivacyMode";
  const NO_REVEAL_CLASS = "noReveal";
  const keys = ["KeyA", "KeyS", "KeyD", "KeyF", "KeyJ", "KeyK", "KeyL", "Semicolon"];
  let firstTap = null;
  let activationKey = null;
  let hoverRevealKey = null;

  function onDoubleTap(event) {
    if (!activationKey) {
      if (!event.shiftKey) {
        activationKey = event.code;
        document.body.classList.add(PRIVACY_MODE_CLASS, NO_REVEAL_CLASS);
      }
      return;
    }

    if (event.shiftKey && event.code === activationKey) {
      activationKey = null;
      hoverRevealKey = null;
      document.body.classList.remove(PRIVACY_MODE_CLASS, NO_REVEAL_CLASS);
    } else if (hoverRevealKey) {
      if (event.shiftKey && event.code === hoverRevealKey) {
        hoverRevealKey = null;
        document.body.classList.add(NO_REVEAL_CLASS);
      }
    } else if (!event.shiftKey && event.code !== activationKey) {
      hoverRevealKey = event.code;
      document.body.classList.remove(NO_REVEAL_CLASS);
    }
  }

  document.addEventListener("keydown", (event) => {
    const target = event.target;
    if (event.ctrlKey || event.altKey || event.metaKey || event.isComposing
      || (target instanceof Element && (target.isContentEditable || target.closest("input, textarea, select")))) {
      firstTap = null;
      return;
    }
    if (event.repeat) {
      return;
    }
    if (event.code === "ShiftLeft" || event.code === "ShiftRight") {
      return;
    }
    if (!keys.includes(event.code)) {
      firstTap = null;
      return;
    }

    const now = performance.now();
    if (!firstTap || now - firstTap.time > TAP_TIMEOUT) {
      firstTap = { code: event.code, shiftKey: event.shiftKey, time: now };
      return;
    }

    if (firstTap.code === event.code && firstTap.shiftKey === event.shiftKey) {
      onDoubleTap(event);
    }
    firstTap = null;
  });
  window.addEventListener("blur", () => { firstTap = null; });
})();
