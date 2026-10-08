class RemoteTrailerVideoPlayer {
  #player = null;
  #events = null;
  #appRouter = null;
  #dashboard = null;
  #container = null;
  #currentSrc = null;
  #currentTime = 0;
  #started = false;
  #stopped = true;
  #hideScroll = false;
  #volume = 100;
  #muted = false;

  id = "powertoysvideoplayer";
  name = "PowerToys Video Player";
  type = "mediaplayer";
  priority = 2;

  constructor({ events, dashboard, appRouter }) {
    this.#events = events;
    this.#dashboard = dashboard;
    this.#appRouter = appRouter;
  }
  canPlayItem = () => false;
  canPlayUrl = (url) => typeof url === "string" && url.length > 0;
  canPlayMediaType = (mediaType) => mediaType?.toLowerCase() === "video";
  supports = (feature) => feature === "PlaybackRate";
  getDeviceProfile = async () => ({});
  currentSrc = () => this.#currentSrc;
  duration = () => Number.isFinite(this.#player?.duration) ? this.#player.duration * 1000 : null;
  pause = () => this.#player?.pause();
  paused = () => this.#player?.paused ?? true;
  unpause = () => this.#player?.play();
  setPlaybackRate = (value) => {
    if (this.#player) {
      this.#player.playbackRate = Number(value);
    }
  };
  getPlaybackRate = () => this.#player?.playbackRate ?? null;
  getSupportedPlaybackRates = () => [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 3.5, 4]
    .map(rate => ({ id: rate, name: `${rate}x` }));
  getStats = async () => {
    const player = this.#player;
    if (!player || this.#stopped) {
      return { categories: [] };
    }
    const categories = [{
      type: "media",
      stats: [{ label: "Playback method", value: "Direct Play" }]
    }];
    const stats = [];
    if (Number.isFinite(player.videoWidth) && player.videoWidth > 0
      && Number.isFinite(player.videoHeight) && player.videoHeight > 0) {
      stats.push({ label: "Video resolution", value: `${player.videoWidth}x${player.videoHeight}` });
    }
    let quality;
    if (typeof player.getVideoPlaybackQuality === "function") {
      try {
        quality = player.getVideoPlaybackQuality();
      } catch {
      }
    }
    for (const [field, label] of [
      ["totalVideoFrames", "Total video frames"],
      ["droppedVideoFrames", "Dropped frames"],
      ["corruptedVideoFrames", "Corrupted frames"]
    ]) {
      if (Number.isFinite(quality?.[field]) && quality[field] >= 0) {
        stats.push({ label, value: String(quality[field]) });
      }
    }
    if (stats.length) {
      categories.push({ type: "video", stats });
    }
    return { categories };
  };
  isMuted = () => this.#player?.muted ?? this.#muted;
  canSetAudioStreamIndex = () => false;
  setAudioStreamIndex = () => {};
  setSubtitleStreamIndex = () => {};
  destroy = () => {
    const player = this.#player;
    this.#player = null;
    this.#started = false;
    this.#stopped = true;
    this.#currentSrc = null;
    if (player) {
      player.pause();
      player.removeAttribute("src");
      player.load();
    }
    this.#container?.remove();
    this.#container = null;
    if (this.#hideScroll) {
      document.body.classList.remove("hide-scroll");
      this.#hideScroll = false;
    }
    this.#dashboard?.default?.setBackdropTransparency("none");
  };
  play = async (options) => {
    this.destroy();
    this.#currentSrc = options.url;
    this.#currentTime = 0;
    this.#stopped = false;

    const container = this.#container = document.createElement("div");
    container.classList.add("RemoteTrailersContainer");
    if (options.fullscreen !== false) {
      container.classList.add("RemoteTrailersContainer-onTop");
      this.#hideScroll = !document.body.classList.contains("hide-scroll");
      document.body.classList.add("hide-scroll");
    }
    document.body.insertBefore(container, document.body.firstChild);

    const player = this.#player = document.createElement("video");
    player.src = options.url;
    player.controls = false;
    player.playsInline = true;
    player.volume = this.#volume / 100;
    player.muted = this.#muted;
    container.appendChild(player);

    if (!options.mediaSource) {
      const mediaSource = options.mediaSource = {
        Id: null,
        MediaStreams: [],
        RunTimeTicks: null,
        SupportsTranscoding: false
      };
      const updateRuntime = () => {
        if (this.#player === player) {
          mediaSource.RunTimeTicks = Number.isFinite(player.duration) && player.duration > 0
            ? Math.round(player.duration * 10000000) : null;
        }
      };
      player.addEventListener("loadedmetadata", updateRuntime);
      player.addEventListener("durationchange", updateRuntime);
      updateRuntime();
    }

    const trigger = (name, args) => {
      if (this.#player === player && this.#started && !this.#stopped) {
        this.#events.trigger(this, name, args);
      }
    };
    const reportError = (type) => {
      try {
        trigger("error", [{ type, streamInfo: { url: options.url, mediaSource: { SupportsTranscoding: false } } }]);
      } finally {
        if (this.#player === player) {
          this.stop(true);
        }
      }
    };
    player.addEventListener("pause", () => trigger("pause"));
    player.addEventListener("play", () => trigger("unpause"));
    player.addEventListener("timeupdate", () => {
      if (this.#player === player) {
        this.#currentTime = player.currentTime * 1000;
        trigger("timeupdate");
      }
    });
    player.addEventListener("volumechange", () => {
      if (this.#player === player) {
        this.#volume = player.volume * 100;
        this.#muted = player.muted;
        trigger("volumechange");
      }
    });
    player.addEventListener("ended", () => {
      if (this.#player === player) {
        this.#onStopped();
      }
    });
    player.addEventListener("error", () => {
      if (this.#player === player) {
        const type = { 2: "NETWORK_ERROR", 3: "MEDIA_DECODE_ERROR", 4: "MEDIA_NOT_SUPPORTED" }[player.error?.code] || "PLAYER_ERROR";
        reportError(type);
      }
    });

    try {
      await player.play();
      if (this.#player !== player || this.#stopped) {
        return;
      }
      this.#started = true;
      if (options.fullscreen !== false) {
        this.#appRouter.showVideoOsd().then(() => {
          if (this.#player === player) {
            container.classList.remove("RemoteTrailersContainer-onTop");
          }
        }).catch((error) => {
          console.error("Error opening remote trailer controls", error);
          reportError("PLAYER_ERROR");
        });
      } else {
        this.#dashboard?.default?.setBackdropTransparency("backdrop");
        container.classList.remove("RemoteTrailersContainer-onTop");
      }
    } catch (error) {
      if (this.#player === player) {
        this.destroy();
      }
      throw error;
    }
  };
  #onStopped = () => {
    if (!this.#player || this.#stopped) {
      return;
    }
    this.#stopped = true;
    this.#currentTime = this.#player.currentTime * 1000;
    this.#events.trigger(this, "stopped", [{ src: this.#currentSrc }]);
    this.#currentSrc = null;
  };
  stop = async (destroy) => {
    try {
      this.#player?.pause();
      this.#onStopped();
    } finally {
      if (destroy) {
        this.destroy();
      }
    }
  };
  currentTime = (value) => {
    if (this.#player && value != null) {
      this.#player.currentTime = value / 1000;
    }
    return this.#player ? this.#player.currentTime * 1000 : this.#currentTime;
  };
  volume = (value) => {
    if (value != null) {
      this.#volume = Math.min(100, Math.max(0, value));
      if (this.#player) {
        this.#player.volume = this.#volume / 100;
      }
    }
    return this.#player ? this.#player.volume * 100 : this.#volume;
  };
  setMute = (value) => {
    if (value != null) {
      this.#muted = value;
      if (this.#player) {
        this.#player.muted = value;
      }
    }
  };
}
window["powertoys/RemoteTrailers"] = async () => RemoteTrailerVideoPlayer;
