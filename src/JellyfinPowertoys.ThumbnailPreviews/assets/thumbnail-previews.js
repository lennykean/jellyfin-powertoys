/**
 * @typedef {Object} TrickplayMetadata
 * @property {number} Resolution
 * @property {number} Width
 * @property {number} Height
 * @property {number} TileWidth
 * @property {number} TileHeight
 * @property {number} ThumbnailCount
 * @property {number} Interval
 * @property {string} VideoId
 *
 * @typedef {Object} TrailerInfo
 * @property {string} Url
 * @property {string} Name
 *
 * @typedef {Object} Item
 * @property {string} Id
 * @property {string} ServerId
 * @property {number} RunTimeTicks
 * @property {{[itemId: string]: {[resolution: string]: TrickplayMetadata}}} Trickplay
 * @property {TrailerInfo[]} RemoteTrailers
 *
 * @typedef {Object} Credentials
 * @property {string} Id
 * @property {string} UserId
 * @property {string} ManualAddress
 * @property {string} LocalAddress
 * @property {string} AccessToken
 *
 * @typedef {Object} Settings
 * @property {number} PreviewDuration
 * @property {boolean} LoopPreview
 * @property {number} FrameMinDuration
 * @property {string | null | undefined} Resolutions
 * @property {boolean} ShowTrailerPreview
 * @property {boolean} EnableTrailerLengthLimit
 * @property {number} TrailerMaxLengthSeconds
 * @property {boolean} OnlyShowSilentTrailers
 * @property {boolean} PlayTrailerAudio
 * @property {boolean} EnableHoverPlay
 * @property {number} MouseLingerDelay
 */

(() => {
  const PLUGIN_ID = "6a65ce4e-fb35-4e99-8f37-02bc3979fe7e";

  /**
   * @type {Map<string, Item>}
   */
  const itemCache = new Map();

  /**
   * @type {Settings}
   */
  let _settings;

  /**
   * @param {string} serverId
   */
  function getCredentials(serverId) {
    const storageItem = localStorage.getItem("jellyfin_credentials");
    if (!storageItem) {
      return;
    }
    /**
     * @type {{Servers: Credentials[]}}
     */
    const credentials = JSON.parse(storageItem);

    for (const server of credentials?.Servers ?? []) {
      if (![server.ManualAddress, server.LocalAddress].includes(location.origin)) {
        continue;
      }
      if (server.Id !== serverId) {
        continue;
      }
      return {
        serverId: server.Id,
        serverUrl: server.ManualAddress || server.LocalAddress,
        apiKey: server.AccessToken,
        userId: server.UserId,
      };
    }
    return;
  }

  /**
   * @param {string} itemId
   * @param {string} serverId
   */
  async function queryTrickplayMetadata(itemId, serverId) {
    if (itemCache.has(itemId)) {
      return itemCache.get(itemId);
    }
    const credentials = getCredentials(serverId);
    if (!credentials) {
      return;
    }
    const url = new URL(`${credentials.serverUrl}/Items`);
    url.searchParams.set("ServerId", serverId);
    url.searchParams.set("Ids", itemId);
    url.searchParams.set("Fields", "Trickplay,RemoteTrailers");
    url.searchParams.set("api_key", credentials.apiKey);

    const response = await fetch(url);
    if (!response.ok) {
      return;
    }
    /**
     * @type {{Items: Item[]}}
     */
    const data = await response.json();
    const item = data.Items[0];
    if (!item) {
      return;
    }
    itemCache.set(itemId, item);

    return item;
  }

  /**
   * @param {Node} sprite
   * @param {Node} mask
   * @param {TrickplayMetadata} trickplay
   */
  function initPreview(sprite, mask, trickplay) {
    const { TileWidth, TileHeight } = trickplay;
    const frameWidth = trickplay.Width / TileWidth;
    const frameHeight = trickplay.Height / TileHeight;

    const containerRect = mask.parentElement.getBoundingClientRect();
    const containerWidth = containerRect.width;
    const containerHeight = containerRect.height;

    const scaleX = containerWidth / frameWidth;
    const scaleY = containerHeight / frameHeight;
    const scale = Math.min(scaleX, scaleY);

    const scaledFrameWidth = frameWidth * scale;
    const scaledFrameHeight = frameHeight * scale;

    const offsetX = (containerWidth - scaledFrameWidth) / 2;
    const offsetY = (containerHeight - scaledFrameHeight) / 2;

    mask.style.position = "absolute";
    mask.style.left = `${offsetX}px`;
    mask.style.top = `${offsetY}px`;
    mask.style.width = `${scaledFrameWidth}px`;
    mask.style.height = `${scaledFrameHeight}px`;
    mask.style.overflow = "hidden";

    sprite.style.width = `${scaledFrameWidth}px`;
    sprite.style.height = `${scaledFrameHeight}px`;
  }

  /**
   * @param {number} frame
   * @param {Node} sprite
   * @param {Node} mask
   * @param {TrickplayMetadata} trickplay
   * @param {HTMLImageElement[]} sheets
   */
  function showFrame(frame, sprite, mask, trickplay, sheets) {
    const { TileWidth, TileHeight } = trickplay;
    const frameWidth = trickplay.Width / TileWidth;
    const frameHeight = trickplay.Height / TileHeight;

    const framesPerSheet = TileWidth * TileHeight;
    const sheetIndex = Math.floor(frame / framesPerSheet);
    const frameInSheet = frame % framesPerSheet;

    const frameX = frameInSheet % TileWidth;
    const frameY = Math.floor(frameInSheet / TileWidth);

    const containerRect = mask.parentElement.getBoundingClientRect();
    const containerWidth = containerRect.width;
    const containerHeight = containerRect.height;

    const scaleX = containerWidth / frameWidth;
    const scaleY = containerHeight / frameHeight;
    const scale = Math.min(scaleX, scaleY);

    const scaledFrameWidth = frameWidth * scale;
    const scaledFrameHeight = frameHeight * scale;
    const scaledSheetWidth = trickplay.Width * scale;
    const scaledSheetHeight = trickplay.Height * scale;

    sprite.style.backgroundImage = `url(${sheets[sheetIndex].src})`;
    sprite.style.backgroundSize = `${scaledSheetWidth}px ${scaledSheetHeight}px`;
    sprite.style.backgroundPosition = `${-frameX * scaledFrameWidth}px ${-frameY * scaledFrameHeight}px`;
  }

  /**
   * @param {Node} container
   * @param {Item} item
   * @param {Settings} settings
   * @param {string} resolution
   */
  async function playPreview(container, item, settings, resolution) {
    const credentials = getCredentials(item.ServerId);
    if (!credentials) {
      return;
    }

    let cancel = false;
    const cancelHandler = () => (cancel = true);

    const mask = document.createElement("div");
    const sprite = document.createElement("div");

    mask.className = "mask";
    sprite.className = "sprite";

    mask.appendChild(sprite);
    container.appendChild(mask);
    container.classList.add("playing");
    setTimeout(() => container.addEventListener("click", cancelHandler), 0);

    try {
      const { serverUrl, apiKey } = credentials;
      const { Trickplay, RunTimeTicks } = item;
      const { TileWidth, TileHeight, ThumbnailCount, Interval } = Trickplay[item.Id][resolution];

      let frameSkip = 1;
      let frameDuration = settings.FrameMinDuration ?? 400;
      let frameCount = ThumbnailCount;
      let frameCountEstimate = Math.floor(RunTimeTicks / 100 / Interval / (TileWidth * TileHeight));
      if (frameCount < frameCountEstimate) {
        frameCount = frameCountEstimate;
      }
      const sheetCount = Math.ceil(frameCount / (TileHeight * TileWidth));

      /**
       * @type {HTMLImageElement[]}
       */
      const sheets = await Promise.all(
        Array.from(Array(sheetCount))
          .map((_, i) => `${serverUrl}/Videos/${item.Id}/Trickplay/${resolution}/${i}.jpg?api_key=${apiKey}`)
          .map(
            (url) =>
              new Promise((resolve, reject) => {
                const img = new Image();
                img.onload = () => resolve(img);
                img.onerror = (err) => reject(err);
                img.src = url;
              }),
          ),
      );
      const framesNeeded = Math.floor(settings.PreviewDuration / frameDuration);
      if (frameCount > framesNeeded) {
        frameSkip = Math.ceil(frameCount / framesNeeded);
      } else {
        frameDuration = Math.floor(settings.PreviewDuration / frameCount);
      }

      console.debug("Playing preview", {
        item,
        resolution,
        frameSkip,
        frameDuration,
        frameCount,
        sheetCount,
        previewFrames: Math.floor(frameCount / frameSkip),
        totalDuration: settings.PreviewDuration,
        loopPreview: settings.LoopPreview,
      });

      initPreview(sprite, mask, Trickplay[item.Id][resolution]);
      do {
        for (let i = 0; i < frameCount; i += frameSkip) {
          if (cancel) {
            break;
          }
          showFrame(i, sprite, mask, Trickplay[item.Id][resolution], sheets);
          await new Promise((resolve) => setTimeout(resolve, frameDuration));
        }
      } while (!cancel && settings.LoopPreview);
    } finally {
      mask.removeChild(sprite);
      container.removeChild(mask);
      container.removeEventListener("click", cancelHandler);
      container.classList.remove("playing");
    }
  }

  /**
   * @param {string} url
   * @returns {boolean}
   */
  function isValidUrl(url) {
    try {
      new URL(url);
      return true;
    } catch {
      return false;
    }
  }

  /**
   * @param {string} url
   * @param {Settings} settings
   * @returns {Promise<{valid: boolean, duration?: number}>}
   */
  async function verifyTrailerUrl(url, settings) {
    try {
      const headResponse = await fetch(url, { method: "HEAD", mode: "cors" });
      if (!headResponse.ok) {
        return { valid: false };
      }
      const contentType = headResponse.headers.get("content-type") || "";
      if (!contentType.startsWith("video/")) {
        return { valid: false };
      }
    } catch (e) {
      console.debug("HEAD check failed, falling back to video probe", e);
    }

    return new Promise((resolve) => {
      const video = document.createElement("video");
      video.preload = "metadata";
      video.crossOrigin = "anonymous";

      const cleanup = () => {
        video.removeAttribute("src");
        video.load();
      };

      const timeout = setTimeout(() => {
        cleanup();
        resolve({ valid: false });
      }, 15000);

      video.onloadedmetadata = () => {
        const duration = video.duration;
        clearTimeout(timeout);
        cleanup();

        if (settings.EnableTrailerLengthLimit && duration > settings.TrailerMaxLengthSeconds) {
          resolve({ valid: false });
          return;
        }

        resolve({ valid: true, duration });
      };

      video.onerror = () => {
        clearTimeout(timeout);
        cleanup();
        resolve({ valid: false });
      };

      video.src = url;
    });
  }

  /**
   * @param {string} url
   * @returns {Promise<boolean>}
   */
  function hasAudioTrack(url) {
    return new Promise((resolve) => {
      const video = document.createElement("video");
      video.preload = "metadata";
      video.crossOrigin = "anonymous";

      const cleanup = () => {
        video.removeAttribute("src");
        video.load();
      };

      const timeout = setTimeout(() => {
        cleanup();
        resolve(true);
      }, 10000);

      video.onloadedmetadata = () => {
        clearTimeout(timeout);

        if (typeof video.audioTracks !== "undefined") {
          const result = video.audioTracks.length > 0;
          cleanup();
          resolve(result);
          return;
        }

        const captureStream = video.captureStream || video.mozCaptureStream;
        if (captureStream) {
          try {
            const stream = captureStream.call(video);
            const result = stream.getAudioTracks().length > 0;
            stream.getTracks().forEach((t) => t.stop());
            cleanup();
            resolve(result);
            return;
          } catch (e) {
            console.debug("captureStream audio check failed", e);
          }
        }

        cleanup();
        resolve(true);
      };

      video.onerror = () => {
        clearTimeout(timeout);
        cleanup();
        resolve(true);
      };

      video.src = url;
    });
  }

  /**
   * @param {Item} item
   * @param {Settings} settings
   * @returns {Promise<string | null>}
   */
  async function getValidTrailerUrl(item, settings) {
    if (!settings.ShowTrailerPreview) {
      return null;
    }

    const credentials = getCredentials(item.ServerId);
    if (!credentials) {
      return null;
    }

    // Try local trailers
    try {
      const localTrailersUrl = `${credentials.serverUrl}/Users/${credentials.userId}/Items/${item.Id}/LocalTrailers?api_key=${credentials.apiKey}`;
      const response = await fetch(localTrailersUrl);
      if (response.ok) {
        const localTrailers = await response.json();
        for (const trailer of localTrailers) {
          const durationTicks = trailer.RunTimeTicks ?? trailer.MediaSources?.[0]?.RunTimeTicks ?? 0;
          const durationSeconds = durationTicks ? durationTicks / 10000000 : 0;
          if (settings.EnableTrailerLengthLimit && durationSeconds > settings.TrailerMaxLengthSeconds) {
            console.debug("Local trailer too long, skipping", { id: trailer.Id, duration: durationSeconds });
            continue;
          }
          const mediaSourceId = trailer.MediaSources?.[0]?.Id ?? trailer.Id;
          const container = trailer.MediaSources?.[0]?.Container ?? trailer.Container ?? "mp4";
          const streamUrl = `${credentials.serverUrl}/Videos/${trailer.Id}/stream.${container}?Static=true&mediaSourceId=${mediaSourceId}&api_key=${credentials.apiKey}`;
          console.debug("Found valid local trailer", { id: trailer.Id, name: trailer.Name, duration: durationSeconds, streamUrl });
          return streamUrl;
        }
      }
    } catch (err) {
      console.debug("Local trailer check failed", err);
    }

    const trailers = item.RemoteTrailers || [];

    for (const trailer of trailers) {
      if (!trailer.Url || !isValidUrl(trailer.Url)) {
        continue;
      }

      const verification = await verifyTrailerUrl(trailer.Url, settings);
      if (!verification.valid) {
        continue;
      }

      console.debug("Found valid trailer", { url: trailer.Url, duration: verification.duration });
      return trailer.Url;
    }

    return null;
  }

  /**
   * @param {Node} container
   * @param {string} trailerUrl
   * @param {Settings} settings
   * @returns {Promise<boolean>}
   */
  async function playTrailerPreview(container, trailerUrl, settings) {
    if (settings.OnlyShowSilentTrailers) {
      const audioDetected = await hasAudioTrack(trailerUrl);
      if (audioDetected) {
        console.debug("Trailer has audio, skipping (OnlyShowSilentTrailers)", trailerUrl);
        return false;
      }
    }

    let cancel = false;
    const cancelHandler = () => (cancel = true);

    const videoContainer = document.createElement("div");
    videoContainer.className = "trailer-container";

    const video = document.createElement("video");
    video.className = "trailer-video";
    video.src = trailerUrl;
    video.muted = !settings.PlayTrailerAudio;
    video.loop = settings.LoopPreview;
    video.playsInline = true;
    if (!trailerUrl.startsWith(location.origin)) {
      video.crossOrigin = "anonymous";
    }

    videoContainer.appendChild(video);
    container.appendChild(videoContainer);
    container.classList.add("playing");
    container.classList.add("playing-trailer");
    setTimeout(() => container.addEventListener("click", cancelHandler), 0);

    try {
      await video.play();

      await new Promise((resolve) => {
        const checkEnd = () => {
          if (cancel || video.ended) {
            resolve();
          } else {
            requestAnimationFrame(checkEnd);
          }
        };
        video.addEventListener("ended", resolve, { once: true });
        checkEnd();
      });
    } catch (error) {
      console.error("Error playing trailer preview", error);
    } finally {
      video.pause();
      video.src = "";
      videoContainer.removeChild(video);
      container.removeChild(videoContainer);
      container.removeEventListener("click", cancelHandler);
      container.classList.remove("playing");
      container.classList.remove("playing-trailer");
    }

    return true;
  }

  /**
   * @param {Item} item
   * @param {string | null} resolution
   * @param {string | null} trailerUrl
   */
  async function createPlayer(item, resolution, trailerUrl) {
    const container = document.createElement("div");
    container.className = "preview-player";

    const playButton = document.createElement("button");
    playButton.className = "play-button";

    const settings = await getSettings(item.ServerId);
    const play = async () => {
      playButton.removeEventListener("click", play);
      try {
        let played = false;
        if (trailerUrl) {
          played = await playTrailerPreview(container, trailerUrl, settings);
        }
        if (!played && resolution) {
          await playPreview(container, item, settings, resolution);
        }
      } finally {
        playButton.addEventListener("click", play);
      }
    };
    playButton.addEventListener("click", play);
    container.appendChild(playButton);

    const icon = document.createElement("img");
    const iconName = trailerUrl ? "clapperboard" : "image-play";
    icon.setAttribute("src", `https://unpkg.com/lucide-static@latest/icons/${iconName}.svg`);
    playButton.appendChild(icon);

    return container;
  }

  /**
   * @param {string} serverId
   */
  async function getSettings(serverId) {
    if (_settings) {
      return _settings;
    }
    const credentials = getCredentials(serverId);
    if (credentials) {
      const response = await fetch(`${credentials.serverUrl}/Plugins/${PLUGIN_ID}/Configuration?api_key=${credentials.apiKey}`);
      if (response.ok) {
        _settings = await response.json();
      }
    }
    setTimeout(() => {
      _settings = undefined;
    }, 10000);

    return (
      _settings ?? {
        PreviewDuration: 10000,
        LoopPreview: false,
      }
    );
  }

  /**
   * @param {Node} node
   */
  async function injectPlayer(node) {
    let container = node.querySelector(".cardScalable");
    if (!container) {
      return;
    }
    if (container.querySelector(".preview-player")) {
      return;
    }

    /**
     * @type {string}
     */
    const itemId = node.getAttribute("data-id");
    /**
     * @type {string}
     */
    const serverId = node.getAttribute("data-serverid");

    const item = await queryTrickplayMetadata(itemId, serverId);
    if (!item) {
      return;
    }

    const settings = await getSettings(serverId);

    let trailerUrl = null;
    try {
      trailerUrl = await getValidTrailerUrl(item, settings);
    } catch (err) {
      console.debug("Trailer check failed", err);
    }

    let resolution = null;
    if (item.Trickplay?.[itemId]) {
      if (settings.Resolutions) {
        for (const key of settings.Resolutions.split(",").map((key) => key.trim())) {
          if (item.Trickplay[item.Id][key]) {
            resolution = key;
            break;
          }
        }
      } else {
        resolution = Object.keys(item.Trickplay[item.Id])[0];
      }
    }

    if (!trailerUrl && !resolution) {
      return;
    }

    const playerContainer = await createPlayer(item, resolution, trailerUrl);
    container.appendChild(playerContainer);

    if (settings.EnableHoverPlay) {
      let lingerTimeout = null;
      let cooldownActive = false;
      let cooldownTimer = null;
      let mouseLeft = false;

      const startCooldown = () => {
        cooldownActive = true;
        mouseLeft = false;
        clearTimeout(cooldownTimer);
        cooldownTimer = setTimeout(
          () => {
            if (mouseLeft) {
              cooldownActive = false;
            } else {
              cooldownTimer = null;
            }
          },
          (settings.MouseLingerDelay || 800) * 2,
        );
      };

      const observer = new MutationObserver(() => {
        if (!playerContainer.classList.contains("playing") && !cooldownActive) return;
        if (!playerContainer.classList.contains("playing")) {
          startCooldown();
        }
      });
      observer.observe(playerContainer, { attributes: true, attributeFilter: ["class"] });

      node.addEventListener("mouseenter", () => {
        if (playerContainer.classList.contains("playing")) return;
        if (cooldownActive) return;
        lingerTimeout = setTimeout(() => {
          playerContainer.querySelector(".play-button")?.click();
        }, settings.MouseLingerDelay || 800);
      });
      node.addEventListener("mouseleave", () => {
        if (lingerTimeout) {
          clearTimeout(lingerTimeout);
          lingerTimeout = null;
        }
        if (cooldownActive) {
          if (!cooldownTimer) {
            cooldownActive = false;
          } else {
            mouseLeft = true;
          }
        }
      });
    }
  }

  const querySelector = "[data-mediatype=Video]";

  const observer = new MutationObserver((mutations) => {
    for (const mutation of mutations) {
      if (mutation.type !== "childList") {
        continue;
      }
      for (const node of mutation.addedNodes) {
        if (node.nodeType !== 1) {
          continue;
        }
        if (node?.matches(querySelector)) {
          injectPlayer(node);
        }
        for (const child of node?.querySelectorAll(querySelector) ?? []) {
          injectPlayer(child);
        }
      }
    }
  });

  document.querySelectorAll(querySelector).forEach(injectPlayer);

  observer.observe(document.body, {
    childList: true,
    subtree: true,
  });
})();
