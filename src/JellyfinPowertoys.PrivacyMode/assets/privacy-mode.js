(function () {
  "use strict";
  const TAP_TIMEOUT = 300;
  const PRIVACY_MODE_CLASS = "powertoysPrivacyMode";
  const NO_REVEAL_CLASS = "noReveal";

  async function listenForControlSequence(keys) {
    while (true) {
      const activationSequence = await listenForDoubleTap(keys);
      if (!activationSequence || activationSequence.shiftKey) {
        continue;
      }
      document.body.classList.add(PRIVACY_MODE_CLASS, NO_REVEAL_CLASS);
      while (true) {
        let nextSequence = await listenForDoubleTap(keys);
        if (!nextSequence) {
          continue;
        }
        if (nextSequence.code !== activationSequence.code && !nextSequence.shiftKey) {
          document.body.classList.remove(NO_REVEAL_CLASS);
          let activateHoverRevealSequence = nextSequence;
          while (true) {
            nextSequence = await listenForDoubleTap(keys);
            if (!nextSequence) {
              continue;
            }
            if (nextSequence.code === activateHoverRevealSequence.code && nextSequence.shiftKey) {
              document.body.classList.add(NO_REVEAL_CLASS);
              break;
            }
            if (nextSequence.code === activationSequence.code && nextSequence.shiftKey) {
              break;
            }
          }
        }
        if (nextSequence.code === activationSequence.code && nextSequence.shiftKey) {
          document.body.classList.remove(PRIVACY_MODE_CLASS, NO_REVEAL_CLASS);
          break;
        }
      }
    }
  }

  async function listenForDoubleTap(keys) {
    const keydown = await withTimeout(getNextKeydown(), TAP_TIMEOUT);
    if (!keydown || !keys.includes(keydown.code)) {
      return;
    }
    const secondKeydown = await withTimeout(getNextKeydown(), TAP_TIMEOUT);
    if (!secondKeydown || secondKeydown.code !== keydown.code || secondKeydown.shiftKey !== keydown.shiftKey) {
      return;
    }
    return secondKeydown;
  }

  function getNextKeydown() {
    return new Promise((resolve) => {
      const handler = (event) => {
        if (event.target !== document.body) {
          document.addEventListener("keydown", handler, { once: true });
        } else {
          resolve(event);
        }
      };
      document.addEventListener("keydown", handler, { once: true });
    });
  }

  function withTimeout(promise, timeout) {
    const cancel = new Promise((resolve) => setTimeout(resolve, timeout));
    return Promise.race([promise, cancel]);
  }

  listenForControlSequence(["KeyA", "KeyS", "KeyD", "KeyF", "KeyJ", "KeyK", "KeyL", "Semicolon"]);
})();
