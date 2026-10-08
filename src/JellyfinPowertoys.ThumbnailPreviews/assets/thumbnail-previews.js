(() => {
  const CARD_SELECTOR = ".card[data-id]";
  const DEFAULTS = {
    PreviewDuration: 10000,
    FrameMinDuration: 400,
    LoopPreview: false,
    Resolutions: null,
    ShowTrailerPreview: true,
    EnableTrailerLengthLimit: true,
    TrailerMaxLengthSeconds: 60,
    OnlyShowSilentTrailers: false,
    PlayTrailerAudio: false,
    EnableHoverPlay: false,
    MouseLingerDelay: 800,
  };
  const cards = new Map();
  const items = new Map();
  const configurations = new Map();
  let activePreview;
  let contextKey;
  let scanScheduled = false;

  function getContext(serverId) {
    const client = window.ApiClient;
    if (!client || !client.accessToken?.() || !client.getCurrentUserId?.()) return null;
    const currentServer = client.serverId();
    if (serverId && serverId !== currentServer) return null;
    return {
      client,
      key: `${currentServer}:${client.getCurrentUserId()}:${client.accessToken()}`,
      userId: client.getCurrentUserId(),
    };
  }

  function normalizeSettings(configuration) {
    const settings = { ...DEFAULTS, ...configuration };
    for (const name of ["PreviewDuration", "FrameMinDuration", "TrailerMaxLengthSeconds", "MouseLingerDelay"]) {
      const value = Number(settings[name]);
      settings[name] = Number.isFinite(value) && value > 0 ? value : DEFAULTS[name];
    }
    return settings;
  }

  async function getSettings(context) {
    let entry = configurations.get(context.key);
    if (!entry || entry.expires <= Date.now()) {
      entry = {
        expires: Date.now() + 10000,
        promise: context.client.getJSON(context.client.getUrl("PowerToys/ThumbnailPreviews/Configuration"), true)
          .then(normalizeSettings)
          .catch(() => ({ ...DEFAULTS })),
      };
      configurations.set(context.key, entry);
    }
    return entry.promise;
  }

  async function getItem(context, itemId) {
    const key = `${context.key}:${itemId}`;
    if (!items.has(key)) {
      const promise = context.client.getItems(context.userId, {
        Ids: itemId,
        Fields: "Trickplay,RemoteTrailers,MediaSources,LocalTrailerCount",
      }).then((result) => result.Items?.[0] ?? null).catch(() => {
        items.delete(key);
        return null;
      });
      items.set(key, promise);
    }
    return items.get(key);
  }

  function selectTrickplay(item, settings) {
    const sources = item.Trickplay ?? {};
    const mediaSourceId = sources[item.Id] ? item.Id : item.MediaSources?.find((source) => sources[source.Id])?.Id;
    const resolutions = sources[mediaSourceId];
    if (!resolutions) return null;
    const preferred = settings.Resolutions
      ? settings.Resolutions.split(",").map((value) => value.trim())
      : Object.keys(resolutions);
    for (const resolution of preferred) {
      const metadata = resolutions[resolution];
      if (metadata && [metadata.Width, metadata.Height, metadata.TileWidth, metadata.TileHeight, metadata.ThumbnailCount]
        .every((value) => Number.isInteger(value) && value > 0)) {
        return { resolution, metadata, mediaSourceId };
      }
    }
    return null;
  }

  function abortError() {
    return new DOMException("Preview cancelled", "AbortError");
  }

  function ensureActive(signal) {
    if (signal.aborted) throw abortError();
  }

  function wait(duration, signal) {
    return new Promise((resolve, reject) => {
      ensureActive(signal);
      const timer = setTimeout(() => {
        signal.removeEventListener("abort", cancel);
        resolve();
      }, duration);
      function cancel() {
        clearTimeout(timer);
        reject(abortError());
      }
      signal.addEventListener("abort", cancel, { once: true });
    });
  }

  function loadSheet(client, url, signal) {
    return new Promise((resolve, reject) => {
      ensureActive(signal);
      const request = new AbortController();
      const image = new Image();
      let objectUrl;
      let finished = false;
      const timer = setTimeout(() => finish(new Error("Preview image timed out")), 15000);
      function finish(error) {
        if (finished) return;
        finished = true;
        clearTimeout(timer);
        signal.removeEventListener("abort", cancel);
        request.abort();
        image.onload = null;
        image.onerror = null;
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        if (error) {
          image.src = "";
          reject(error);
        } else {
          resolve(image);
        }
      }
      function cancel() { finish(abortError()); }
      image.onload = () => finish();
      image.onerror = () => finish(new Error("Preview image unavailable"));
      signal.addEventListener("abort", cancel, { once: true });
      try {
        const headers = {};
        client.setRequestHeaders(headers);
        void fetch(url, { method: "GET", headers, credentials: "same-origin", signal: request.signal })
          .then((response) => {
            if (!response.ok) throw new Error("Preview image unavailable");
            return response.blob();
          })
          .then((blob) => {
            if (finished) return;
            ensureActive(signal);
            objectUrl = URL.createObjectURL(blob);
            image.src = objectUrl;
          })
          .catch(finish);
      } catch (error) {
        finish(error);
      }
    });
  }

  function releaseVideo(video) {
    video.pause();
    video.removeAttribute("src");
    video.load();
    video.remove();
  }

  function probeVideo(url, signal) {
    return new Promise((resolve, reject) => {
      ensureActive(signal);
      const video = document.createElement("video");
      video.preload = "metadata";
      const timer = setTimeout(() => finish(null), 15000);
      function finish(result, error) {
        clearTimeout(timer);
        signal.removeEventListener("abort", cancel);
        video.onloadedmetadata = null;
        video.onerror = null;
        releaseVideo(video);
        if (error) reject(error);
        else resolve(result);
      }
      function cancel() { finish(null, abortError()); }
      video.onloadedmetadata = () => {
        let hasAudio = null;
        if (video.audioTracks) hasAudio = video.audioTracks.length > 0;
        else if (typeof video.mozHasAudio === "boolean") hasAudio = video.mozHasAudio;
        else {
          const captureStream = video.captureStream || video.mozCaptureStream;
          if (captureStream) {
            try {
              const stream = captureStream.call(video);
              hasAudio = stream.getAudioTracks().length > 0;
              stream.getTracks().forEach(track => track.stop());
            } catch {
            }
          }
        }
        finish({ duration: video.duration, hasAudio });
      };
      video.onerror = () => finish(null);
      signal.addEventListener("abort", cancel, { once: true });
      video.src = url;
    });
  }

  function trailerAllowed(duration, hasAudio, settings) {
    return (!settings.EnableTrailerLengthLimit || (Number.isFinite(duration) && duration <= settings.TrailerMaxLengthSeconds))
      && (!settings.OnlyShowSilentTrailers || hasAudio === false);
  }

  async function getTrailer(context, item, settings, signal) {
    if (!settings.ShowTrailerPreview) return null;
    let localTrailers = [];
    try {
      localTrailers = await context.client.getLocalTrailers(context.userId, item.Id);
    } catch {
    }
    ensureActive(signal);
    for (const trailer of localTrailers) {
      const source = trailer.MediaSources?.[0];
      const durationTicks = source?.RunTimeTicks ?? trailer.RunTimeTicks;
      const duration = durationTicks ? durationTicks / 10000000 : null;
      const streams = source?.MediaStreams ?? trailer.MediaStreams;
      const hasAudio = streams ? streams.some((stream) => stream.Type === "Audio") : null;
      if (duration && settings.EnableTrailerLengthLimit && duration > settings.TrailerMaxLengthSeconds) continue;
      if (settings.OnlyShowSilentTrailers && hasAudio === true) continue;
      const container = source?.Container ?? trailer.Container ?? "mp4";
      const url = context.client.getUrl(`Videos/${trailer.Id}/stream.${container}`, {
        Static: true,
        mediaSourceId: source?.Id ?? trailer.Id,
        api_key: context.client.accessToken(),
      });
      const probe = await probeVideo(url, signal);
      if (probe && trailerAllowed(duration ?? probe.duration, hasAudio ?? probe.hasAudio, settings)) return url;
    }
    for (const trailer of item.RemoteTrailers ?? []) {
      let url;
      try {
        url = new URL(trailer.Url);
      } catch {
        continue;
      }
      if (!["http:", "https:"].includes(url.protocol)) continue;
      const probe = await probeVideo(url.href, signal);
      if (probe && trailerAllowed(probe.duration, probe.hasAudio, settings)) return url.href;
    }
    return null;
  }

  function previewFrames(metadata, settings) {
    const count = metadata.ThumbnailCount;
    const desired = Math.max(1, Math.floor(settings.PreviewDuration / settings.FrameMinDuration));
    const step = Math.max(1, Math.ceil(count / desired));
    return Array.from({ length: Math.ceil(count / step) }, (_, index) => index * step);
  }

  async function playSlideshow(state, trickplay, settings, signal) {
    const { metadata, resolution, mediaSourceId } = trickplay;
    const frames = previewFrames(metadata, settings);
    const framesPerSheet = metadata.TileWidth * metadata.TileHeight;
    const sheets = new Map();
    const canvas = document.createElement("canvas");
    canvas.width = metadata.Width;
    canvas.height = metadata.Height;
    canvas.className = "powertoys-preview-media";
    canvas.setAttribute("aria-hidden", "true");
    const drawing = canvas.getContext("2d");
    if (!drawing) throw new Error("Preview drawing unavailable");
    state.overlay.appendChild(canvas);
    const duration = Math.max(settings.FrameMinDuration, settings.PreviewDuration / frames.length);
    do {
      for (const frame of frames) {
        ensureActive(signal);
        const sheetIndex = Math.floor(frame / framesPerSheet);
        if (!sheets.has(sheetIndex)) {
          const url = state.context.client.getUrl(`Videos/${state.item.Id}/Trickplay/${resolution}/${sheetIndex}.jpg`, {
            mediaSourceId,
          });
          sheets.set(sheetIndex, await loadSheet(state.context.client, url, signal));
        }
        const index = frame % framesPerSheet;
        drawing.drawImage(sheets.get(sheetIndex),
          (index % metadata.TileWidth) * metadata.Width,
          Math.floor(index / metadata.TileWidth) * metadata.Height,
          metadata.Width, metadata.Height, 0, 0, metadata.Width, metadata.Height);
        await wait(duration, signal);
      }
    } while (settings.LoopPreview);
  }

  async function playTrailer(state, url, settings, signal) {
    const video = document.createElement("video");
    video.className = "powertoys-preview-media";
    video.muted = !settings.PlayTrailerAudio;
    video.loop = settings.LoopPreview;
    video.playsInline = true;
    video.preload = "auto";
    video.setAttribute("aria-hidden", "true");
    state.overlay.appendChild(video);
    try {
      await new Promise((resolve, reject) => {
        ensureActive(signal);
        function finish(error) {
          signal.removeEventListener("abort", cancel);
          video.onended = null;
          video.onerror = null;
          if (error) reject(error);
          else resolve();
        }
        function cancel() { finish(abortError()); }
        video.onended = () => finish();
        video.onerror = () => finish(new Error("Trailer playback unavailable"));
        signal.addEventListener("abort", cancel, { once: true });
        video.src = url;
        video.play().catch((error) => finish(error));
      });
    } finally {
      releaseVideo(video);
    }
  }

  function setControl(state, playing, loading = false) {
    const label = playing ? "Stop preview" : "Preview";
    state.control.setAttribute("aria-label", `${label}: ${state.item.Name ?? "video"}`);
    state.control.setAttribute("aria-pressed", String(playing));
    state.control.title = loading ? "Loading preview…" : label;
  }

  function stopPreview(state) {
    clearTimeout(state.lingerTimer);
    state.playback?.abort();
  }

  async function startPreview(state) {
    if (state.playback) {
      stopPreview(state);
      return;
    }
    if (!state.card.isConnected) return;
    if (activePreview) stopPreview(activePreview);
    const playback = new AbortController();
    state.playback = playback;
    activePreview = state;
    const signal = playback.signal;
    state.overlay = document.createElement("div");
    state.overlay.className = "powertoys-preview-overlay";
    state.overlay.addEventListener("click", (event) => {
      event.preventDefault();
      event.stopPropagation();
      stopPreview(state);
    });
    state.surface.appendChild(state.overlay);
    state.surface.classList.add("powertoys-preview-playing");
    setControl(state, true, true);
    try {
      const settings = await getSettings(state.context);
      ensureActive(signal);
      const trickplay = selectTrickplay(state.item, settings);
      let played = false;
      const trailer = await getTrailer(state.context, state.item, settings, signal);
      ensureActive(signal);
      setControl(state, true);
      if (trailer) {
        try {
          await playTrailer(state, trailer, settings, signal);
          played = true;
        } catch (error) {
          if (error.name === "AbortError") throw error;
        }
      }
      if (!played && trickplay) await playSlideshow(state, trickplay, settings, signal);
    } catch (error) {
      if (error.name !== "AbortError") console.debug("Thumbnail preview unavailable");
    } finally {
      state.overlay?.remove();
      state.overlay = null;
      state.surface.classList.remove("powertoys-preview-playing");
      state.playback = null;
      if (activePreview === state) activePreview = null;
      state.cooldownUntil = Date.now() + (state.settings.MouseLingerDelay * 2);
      setControl(state, false);
    }
  }

  function createControl(state, hasTrailer) {
    const control = document.createElement(state.card.tagName === "BUTTON" ? "span" : "button");
    if (control.tagName === "BUTTON") control.type = "button";
    else {
      control.setAttribute("role", "button");
      control.tabIndex = 0;
    }
    control.className = "powertoys-preview-toggle";
    const icon = document.createElement("img");
    const iconName = hasTrailer ? "clapperboard" : "image-play";
    icon.setAttribute("src", `https://unpkg.com/lucide-static@latest/icons/${iconName}.svg`);
    icon.alt = "";
    icon.setAttribute("aria-hidden", "true");
    control.appendChild(icon);
    control.addEventListener("click", (event) => {
      event.preventDefault();
      event.stopPropagation();
      void startPreview(state);
    });
    control.addEventListener("keydown", (event) => {
      if (["Enter", " "].includes(event.key)) {
        event.preventDefault();
        event.stopPropagation();
        void startPreview(state);
      }
    });
    state.control = control;
    setControl(state, false);
    return control;
  }

  function removeCard(state) {
    stopPreview(state);
    state.discovery.abort();
    state.control?.remove();
    state.surface.classList.remove("powertoys-preview-host");
    state.card.removeEventListener("mouseenter", state.enter);
    state.card.removeEventListener("mouseleave", state.leave);
    cards.delete(state.card);
  }

  async function attachCard(card, context) {
    const surface = card.querySelector(".cardScalable");
    if (!surface) return;
    const itemId = card.getAttribute("data-id");
    const state = { card, surface, context, itemId, discovery: new AbortController(), cooldownUntil: 0 };
    cards.set(card, state);
    try {
      const [item, settings] = await Promise.all([getItem(context, itemId), getSettings(context)]);
      ensureActive(state.discovery.signal);
      if (!item) {
        cards.delete(card);
        return;
      }
      if (!card.isConnected || cards.get(card) !== state) return;
      state.item = item;
      state.settings = settings;
      const trickplay = selectTrickplay(item, settings);
      const hasTrailer = settings.ShowTrailerPreview && (item.LocalTrailerCount > 0 || item.RemoteTrailers?.length > 0);
      if (!trickplay && !hasTrailer) return;
      surface.classList.add("powertoys-preview-host");
      surface.appendChild(createControl(state, hasTrailer));
      state.enter = () => {
        if (!state.settings.EnableHoverPlay || state.playback || Date.now() < state.cooldownUntil) return;
        clearTimeout(state.lingerTimer);
        state.lingerTimer = setTimeout(() => void startPreview(state), state.settings.MouseLingerDelay);
      };
      state.leave = () => clearTimeout(state.lingerTimer);
      card.addEventListener("mouseenter", state.enter);
      card.addEventListener("mouseleave", state.leave);
    } catch (error) {
      if (error.name !== "AbortError") console.debug("Thumbnail preview unavailable");
    }
  }

  function reconcile() {
    scanScheduled = false;
    const context = getContext();
    if (context?.key !== contextKey) {
      for (const state of cards.values()) removeCard(state);
      items.clear();
      configurations.clear();
      contextKey = context?.key;
    }
    for (const state of cards.values()) {
      if (!state.card.isConnected || state.card.getAttribute("data-id") !== state.itemId
        || getContext(state.card.getAttribute("data-serverid"))?.key !== state.context.key
        || state.card.querySelector(".cardScalable") !== state.surface || (state.control && !state.control.isConnected)) {
        removeCard(state);
      }
    }
    if (!context) return;
    for (const card of document.querySelectorAll(CARD_SELECTOR)) {
      const mediaType = card.getAttribute("data-mediatype")?.toLowerCase();
      if (mediaType !== "video" || cards.has(card)) continue;
      if (getContext(card.getAttribute("data-serverid"))) void attachCard(card, context);
    }
  }

  function scheduleScan() {
    if (scanScheduled) return;
    scanScheduled = true;
    requestAnimationFrame(reconcile);
  }

  function stopAll() {
    for (const state of cards.values()) stopPreview(state);
  }

  const observer = new MutationObserver(scheduleScan);
  observer.observe(document.body, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ["data-id", "data-serverid", "data-mediatype"],
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && activePreview) {
      event.preventDefault();
      stopPreview(activePreview);
    }
  });
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) stopAll();
    else scheduleScan();
  });
  window.addEventListener("hashchange", () => { stopAll(); scheduleScan(); });
  window.addEventListener("popstate", () => { stopAll(); scheduleScan(); });
  window.addEventListener("storage", scheduleScan);
  window.addEventListener("focus", scheduleScan);
  window.addEventListener("pagehide", stopAll);
  scheduleScan();
  setTimeout(scheduleScan, 1000);
})();
