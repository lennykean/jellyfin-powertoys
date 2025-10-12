class RemoteTrailerVideoPlayer {
  #player = null;
  #events = null;
  #appRouter = null;
  #dashboard = null;

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
  canPlayUrl = () => true;
  canPlayMediaType = (mediaType) => mediaType?.toLowerCase() === "video";
  getDeviceProfile = async () => ({});
  currentSrc = () => this.#player?.src;
  duration = () => this.#player?.duration * 1000;
  pause = () => this.#player?.pause();
  paused = () => this.#player?.paused;
  unpause = () => this.#player?.play();
  isMuted = () => this.#player?.muted;
  canSetAudioStreamIndex = () => false;
  destroy = () => {
    if (this.#player) {
      this.#player.parentElement.remove();
    }
    this.#player = null;
    this.#dashboard?.default?.setBackdropTransparency("none");
  };
  play = async (options) => {
    console.debug("play", options);

    const container = document.createElement("div");
    container.classList.add("RemoteTrailersContainer");
    document.body.insertBefore(container, document.body.firstChild);

    this.#player = document.createElement("video");
    this.#player.src = options.url;
    this.#player.controls = false;
    container.appendChild(this.#player);

    this.#player.addEventListener("playing", async () => {
      this.#player.addEventListener("pause", () => this.#events.trigger(this, "pause"));
      this.#player.addEventListener("play", async () => this.#events.trigger(this, "unpause"));
      this.#player.addEventListener("timeupdate", () => this.#events.trigger(this, "timeupdate"));
      this.#player.addEventListener("ended", () => this.#events.trigger(this, "stopped"));
      this.#player.addEventListener("error", (error) => console.error("error playing trailer", options, error));
      await this.#appRouter.showVideoOsd();
    });
    return this.#player.play();
  };
  stop = async (destroy) => {
    if (destroy) {
      this.destroy();
    }
    this.#player?.pause();
  };
  currentTime = (value) => {
    if (this.#player && value != null) {
      this.#player.currentTime = value / 1000;
    }
    return this.#player?.currentTime * 1000;
  };
  volume = (value) => {
    if (this.#player && value != null) {
      this.#player.volume = value / 100;
    }
    return this.#player?.volume * 100;
  };
  setMute = (value) => {
    if (this.#player && value != null) {
      this.#player.muted = value;
    }
  };
}
window["powertoys/RemoteTrailers"] = async () => RemoteTrailerVideoPlayer;
