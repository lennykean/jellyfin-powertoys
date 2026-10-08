(function () {
  "use strict";

  const PLUGIN_ID = "36eb87e0-0373-423b-a547-2acb96e33430";
  const MARKER_ATTR = "data-jellytag-injected";
  const processingSheets = new WeakSet();
  let pendingMenu = null;
  let awaitingMenuEvent = null;
  let repeatedMenuRequest = false;
  let menuContextAmbiguous = false;
  let pendingMenuSession = null;
  let quickTagInFlight = false;
  let toastTimer = null;

  function normalizeId(id) {
    const value = (id || "").replace(/-/g, "");
    return /^[a-f0-9]{32}$/i.test(value) ? value : null;
  }

  function getRouteParams() {
    const hash = window.location.hash;
    const queryIndex = hash.indexOf("?");
    return new URLSearchParams(queryIndex === -1 ? "" : hash.slice(queryIndex + 1));
  }

  function getSelectedItems() {
    const items = new Map();
    for (const checkbox of document.querySelectorAll(".itemSelectionPanel .chkItemSelect:checked")) {
      const card = checkbox.closest("[data-id]");
      const id = normalizeId(card?.getAttribute("data-id"));
      if (id) {
        items.set(id, { id, serverId: card.getAttribute("data-serverid"), element: card });
      }
    }
    return [...items.values()];
  }

  function captureMenuContext(target, menuCommand = false) {
    if (!target) {
      return null;
    }
    const selectionButton = target.closest(".btnSelectionPanelOptions");
    if (selectionButton) {
      const selected = getSelectedItems();
      if (!selected.length) {
        return null;
      }
      const servers = new Set(selected.map((item) => item.serverId).filter(Boolean));
      if (servers.size > 1) {
        return null;
      }
      return { itemIds: selected.map((item) => item.id), serverId: selected[0].serverId, elements: selected.map((item) => item.element), selection: true };
    }

    const menuButton = target.closest('[data-action="menu"], .btnMoreCommands');
    if (!menuButton && !menuCommand) {
      return null;
    }
    const card = (menuCommand ? target : menuButton).closest("[data-id]");
    const cardId = normalizeId(card?.getAttribute("data-id"));
    if (cardId) {
      return { itemIds: [cardId], serverId: card.getAttribute("data-serverid"), elements: [card], selection: false };
    }
    if (menuCommand || !menuButton.matches(".btnMoreCommands")) {
      return null;
    }
    const detailPage = menuButton.closest(".itemDetailPage");
    if (!detailPage) {
      return null;
    }
    const source = detailPage.querySelector(".selectSource");
    const params = getRouteParams();
    const id = normalizeId(source?.value) || normalizeId(params.get("id"));
    return id ? { itemIds: [id], serverId: params.get("serverId"), elements: [], selection: false } : null;
  }

  function getMenuSession() {
    const client = window.ApiClient;
    return { client, userId: client?.getCurrentUserId?.(), serverId: client?.serverId?.(), token: client?.accessToken?.() };
  }

  function isCurrentMenuSession(session) {
    const current = getMenuSession();
    return session && Object.keys(current).every((key) => current[key] === session[key]);
  }

  function requestMenu(context, event) {
    if (menuContextAmbiguous) {
      return;
    }
    const session = getMenuSession();
    if (awaitingMenuEvent) {
      const sameTarget = context && pendingMenu
        && context.itemIds.join(",") === pendingMenu.itemIds.join(",")
        && context.serverId === pendingMenu.serverId
        && context.selection === pendingMenu.selection
        && isCurrentMenuSession(pendingMenuSession);
      if (event.type === "command" && awaitingMenuEvent.type === "click" && awaitingMenuEvent.eventPhase !== 0
        && sameTarget) {
        return;
      }
      if (sameTarget) {
        repeatedMenuRequest = true;
        awaitingMenuEvent = event;
      } else {
        menuContextAmbiguous = true;
        pendingMenu = null;
        pendingMenuSession = null;
      }
      return;
    }
    awaitingMenuEvent = event;
    pendingMenu = context;
    pendingMenuSession = session;
  }

  function invalidateMenuContext() {
    pendingMenu = null;
    if (awaitingMenuEvent) {
      repeatedMenuRequest = true;
    }
  }

  function captureMenuEvent(event) {
    const target = event.target instanceof Element ? event.target : event.target?.parentElement;
    if (event.type === "command") {
      if (event.detail?.command === "menu") {
        requestMenu(captureMenuContext(target, true), event);
      } else if (event.detail?.command === "back") {
        invalidateMenuContext();
      }
    } else if (event.type === "contextmenu") {
      const context = captureMenuContext(target, true);
      if (awaitingMenuEvent && context && pendingMenu
        && context.itemIds.join(",") === pendingMenu.itemIds.join(",")
        && context.serverId === pendingMenu.serverId && context.selection === pendingMenu.selection
        && isCurrentMenuSession(pendingMenuSession)) {
        repeatedMenuRequest = true;
      } else {
        invalidateMenuContext();
      }
    } else if (event.type === "keydown" && event.key === "Escape") {
      invalidateMenuContext();
    } else if (event.type === "click" && target) {
      const context = captureMenuContext(target);
      const unownedMenu = target.closest(".btnToggleContextMenu")
        || target.closest(".btnMore")?.closest(".formDialogHeader")?.parentElement?.querySelector(".editMetadataForm");
      if (context || unownedMenu || target.closest('[data-action="menu"], .btnMoreCommands, .btnSelectionPanelOptions')) {
        requestMenu(context, event);
      } else if (!target.closest(".actionSheet")) {
        invalidateMenuContext();
      }
    }
  }

  function getApiClient(context) {
    const apiClient = window.ApiClient;
    if (!apiClient || !apiClient.getCurrentUserId()) {
      throw new Error("Sign in before editing tags.");
    }
    if (context.session && !isCurrentMenuSession(context.session)) {
      throw new Error("Reopen the item menu after signing in.");
    }
    if (context.serverId && apiClient.serverId() !== context.serverId) {
      throw new Error("Connect to this item's server before editing tags.");
    }
    return apiClient;
  }

  async function loadQuickTags(apiClient) {
    try {
      const config = await apiClient.getPluginConfiguration(PLUGIN_ID);
      return [...new Set((config.QuickTags || []).filter((tag) => typeof tag === "string" && tag.trim()))];
    } catch (error) {
      console.warn("[JellyTag] Failed to load quick tags:", error);
      return [];
    }
  }

  function cleanItemForUpdate(item) {
    const copy = JSON.parse(JSON.stringify(item));
    const fieldsToStrip = [
      "Trickplay", "TrickplayInfo", "PlayAccess", "People", "Studios", "GenreItems", "TagItems",
      "ArtistItems", "AlbumArtists", "MediaStreams", "MediaSources", "Chapters", "RemoteTrailers",
      "ImageTags", "BackdropImageTags", "ParentBackdropImageTags",
    ];
    for (const field of fieldsToStrip) {
      delete copy[field];
    }
    return copy;
  }

  async function applyTagChanges(context, tagsToAdd, tagsToRemove) {
    const apiClient = getApiClient(context);
    const userId = apiClient.getCurrentUserId();
    const lowerRemove = tagsToRemove.map((tag) => tag.toLowerCase());
    const result = { successes: 0, failures: 0, forbidden: false };
    async function processItem(id) {
      try {
        const item = await apiClient.getItem(userId, id);
        item.Tags = (item.Tags || []).filter((tag) => !lowerRemove.includes(tag.toLowerCase()));
        for (const tag of tagsToAdd) {
          if (!item.Tags.some((current) => current.toLowerCase() === tag.toLowerCase())) {
            item.Tags.push(tag);
          }
        }
        await apiClient.updateItem(cleanItemForUpdate(item));
        result.successes++;
      } catch (error) {
        result.forbidden ||= error?.status === 403 || error?.response?.status === 403;
        result.failures++;
        console.error("[JellyTag] Failed to update item " + id + ":", error);
      }
    }
    for (let i = 0; i < context.itemIds.length; i += 5) {
      await Promise.all(context.itemIds.slice(i, i + 5).map(processItem));
    }
    return result;
  }

  async function getTagInfo(context) {
    const apiClient = getApiClient(context);
    const items = [];
    for (let i = 0; i < context.itemIds.length; i += 5) {
      items.push(...await Promise.all(context.itemIds.slice(i, i + 5).map((id) => apiClient.getItem(apiClient.getCurrentUserId(), id))));
    }
    const tags = new Map();
    for (const item of items) {
      const itemTags = new Set();
      for (const tag of item.Tags || []) {
        const key = tag.toLowerCase();
        if (itemTags.has(key)) {
          continue;
        }
        itemTags.add(key);
        const current = tags.get(key);
        tags.set(key, { name: current?.name || tag, count: (current?.count || 0) + 1 });
      }
    }
    return { tags: [...tags.values()].map((tag) => tag.name), counts: new Map([...tags].map(([key, tag]) => [key, tag.count])), items };
  }

  function showToast(message) {
    let toast = document.querySelector(".jellytag-toast");
    if (!toast) {
      toast = document.createElement("div");
      toast.className = "jellytag-toast";
      toast.setAttribute("role", "status");
      toast.setAttribute("aria-live", "polite");
      document.body.appendChild(toast);
    }
    toast.textContent = message;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toast.remove(), 5000);
  }

  function finishEditing(context) {
    const containers = new Set();
    for (const element of context.elements) {
      const container = element.closest('[is="emby-itemscontainer"]');
      if (typeof container?.notifyRefreshNeeded === "function") {
        containers.add(container);
      }
    }
    for (const container of containers) {
      container.notifyRefreshNeeded(true);
    }
    for (const callback of [...(document._callbacks?.REFRESH_NEEDED || [])]) {
      try {
        callback.call(document, { type: "REFRESH_NEEDED" });
      } catch (error) {
        console.warn("[JellyTag] Failed to refresh the item view:", error);
      }
    }
    if (context.selection) {
      document.querySelector(".btnCloseSelectionPanel")?.click();
    }
  }

  function resultMessage(result) {
    if (result.forbidden) {
      return "You don't have permission to edit tags.";
    }
    if (!result.failures) {
      return "Tags saved.";
    }
    return result.successes ? "Saved tags for " + result.successes + " items; " + result.failures + " failed. Try saving again." : "Failed to save tags. Try again.";
  }

  function createButton(label, className, onClick) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = className;
    button.textContent = label;
    button.addEventListener("click", onClick);
    return button;
  }

  class TagDialog {
    constructor(context) {
      this.context = context;
      this.originalTags = [];
      this.currentTags = [];
      this.tagCounts = new Map();
      this.promotedTags = new Set();
      this.saving = false;
      this.loaded = false;
    }

    async open() {
      this.previousFocus = document.activeElement;
      this.buildDOM();
      document.body.appendChild(this.overlay);
      document.body.classList.add("jellytag-dialog-open");
      this.onKeydown = (event) => {
        if (event.key === "Escape") {
          event.preventDefault();
          event.stopPropagation();
          this.close();
        } else if (event.key === "Tab") {
          const controls = [...this.dialog.querySelectorAll("button:not(:disabled), input:not(:disabled)")];
          const first = controls[0];
          const last = controls[controls.length - 1];
          if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last?.focus();
          } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first?.focus();
          }
        }
      };
      this.onNavigation = () => this.close();
      document.addEventListener("keydown", this.onKeydown, true);
      window.addEventListener("hashchange", this.onNavigation);
      window.addEventListener("popstate", this.onNavigation);
      this.dialog.focus();
      await this.load();
    }

    async load() {
      this.status.textContent = "Loading tags...";
      this.retry.hidden = true;
      try {
        const info = await getTagInfo(this.context);
        if (!this.overlay.isConnected) {
          return;
        }
        this.originalTags = [...info.tags];
        this.currentTags = [...info.tags];
        this.tagCounts = info.counts;
        this.loaded = true;
        this.subtitle.textContent = info.items.length === 1 ? info.items[0].Name + " · " + info.items[0].Type : info.items.length + " selected items";
        this.status.textContent = "";
        this.renderList();
        this.updateButtons();
        this.input.focus();
      } catch (error) {
        console.error("[JellyTag] Failed to load tags:", error);
        this.status.textContent = "Couldn't load tags. Try again.";
        this.retry.hidden = false;
      }
    }

    buildDOM() {
      this.overlay = document.createElement("div");
      this.overlay.className = "jellytag-overlay";
      this.overlay.addEventListener("mousedown", (event) => {
        if (event.target === this.overlay) {
          this.close();
        }
      });
      this.dialog = document.createElement("section");
      this.dialog.className = "jellytag-dialog focuscontainer";
      this.dialog.tabIndex = -1;
      this.dialog.setAttribute("role", "dialog");
      this.dialog.setAttribute("aria-modal", "true");
      this.dialog.setAttribute("aria-labelledby", "jellytag-title");
      const header = document.createElement("header");
      header.className = "jellytag-header";
      const heading = document.createElement("div");
      const title = document.createElement("h2");
      title.id = "jellytag-title";
      title.textContent = "Manage tags";
      this.subtitle = document.createElement("p");
      this.subtitle.className = "jellytag-subtitle";
      this.subtitle.textContent = this.context.itemIds.length + (this.context.itemIds.length === 1 ? " selected item" : " selected items");
      heading.append(title, this.subtitle);
      this.closeButton = createButton("×", "jellytag-icon-button", () => this.close());
      this.closeButton.setAttribute("aria-label", "Close tag editor");
      header.append(heading, this.closeButton);
      const content = document.createElement("div");
      content.className = "jellytag-content";
      const label = document.createElement("label");
      label.className = "jellytag-label";
      label.htmlFor = "jellytag-input";
      label.textContent = "Add a tag";
      const row = document.createElement("div");
      row.className = "jellytag-input-row";
      this.input = document.createElement("input");
      this.input.id = "jellytag-input";
      this.input.type = "text";
      this.input.placeholder = "Tag name";
      this.input.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          this.addInput();
        }
      });
      this.addButton = createButton("Add", "jellytag-button jellytag-button-secondary", () => this.addInput());
      row.append(this.input, this.addButton);
      this.list = document.createElement("div");
      this.list.className = "jellytag-list";
      this.status = document.createElement("p");
      this.status.className = "jellytag-status";
      this.status.setAttribute("role", "status");
      this.status.setAttribute("aria-live", "polite");
      this.retry = createButton("Retry", "jellytag-button jellytag-button-secondary", () => this.load());
      this.retry.hidden = true;
      content.append(label, row, this.list, this.status, this.retry);
      const footer = document.createElement("footer");
      footer.className = "jellytag-footer";
      this.cancelButton = createButton("Cancel", "jellytag-button jellytag-button-text", () => this.close());
      this.resetButton = createButton("Reset", "jellytag-button jellytag-button-secondary", () => {
        this.currentTags = [...this.originalTags];
        this.promotedTags.clear();
        this.status.textContent = "";
        this.renderList();
        this.updateButtons();
      });
      this.saveButton = createButton("Save tags", "jellytag-button jellytag-button-primary", () => this.save());
      footer.append(this.cancelButton, this.resetButton, this.saveButton);
      this.dialog.append(header, content, footer);
      this.overlay.appendChild(this.dialog);
      this.updateButtons();
    }

    changes() {
      const add = this.currentTags.filter((tag) => this.promotedTags.has(tag.toLowerCase()) || !this.originalTags.some((original) => original.toLowerCase() === tag.toLowerCase()));
      const remove = this.originalTags.filter((tag) => !this.currentTags.some((current) => current.toLowerCase() === tag.toLowerCase()));
      return { add, remove };
    }

    updateButtons() {
      const changes = this.changes();
      const changed = changes.add.length > 0 || changes.remove.length > 0;
      this.input.disabled = !this.loaded || this.saving;
      this.addButton.disabled = !this.loaded || this.saving;
      this.saveButton.disabled = !this.loaded || !changed || this.saving;
      this.resetButton.disabled = !this.loaded || !changed || this.saving;
      this.cancelButton.disabled = this.saving;
      this.closeButton.disabled = this.saving;
      this.saveButton.textContent = this.saving ? "Saving..." : "Save tags";
      for (const button of this.list.querySelectorAll("button")) {
        button.disabled = this.saving;
      }
    }

    renderList() {
      this.list.replaceChildren();
      if (!this.currentTags.length) {
        const empty = document.createElement("p");
        empty.className = "jellytag-empty";
        empty.textContent = "No tags. Add one above to get started.";
        this.list.appendChild(empty);
      }
      for (const tag of [...this.currentTags].sort((a, b) => a.localeCompare(b))) {
        const key = tag.toLowerCase();
        const count = this.tagCounts.get(key) || 0;
        const promoted = this.promotedTags.has(key);
        const partial = count > 0 && count < this.context.itemIds.length;
        const row = document.createElement("div");
        row.className = "jellytag-tag-row";
        const text = document.createElement("span");
        text.className = "jellytag-tag-name";
        text.textContent = tag;
        row.appendChild(text);
        if (partial) {
          const promote = createButton(promoted ? "Will apply to all" : count + " of " + this.context.itemIds.length + " · Apply to all", "jellytag-coverage", () => {
            if (this.promotedTags.has(key)) {
              this.promotedTags.delete(key);
            } else {
              this.promotedTags.add(key);
            }
            this.renderList();
            this.updateButtons();
          });
          promote.setAttribute("aria-pressed", String(promoted));
          promote.setAttribute("aria-label", (promoted ? "Undo applying " : "Apply ") + tag + " to all selected items");
          row.appendChild(promote);
        }
        const remove = createButton("×", "jellytag-icon-button", () => {
          this.currentTags = this.currentTags.filter((current) => current.toLowerCase() !== key);
          this.promotedTags.delete(key);
          this.renderList();
          this.updateButtons();
        });
        remove.setAttribute("aria-label", "Remove tag " + tag);
        row.appendChild(remove);
        this.list.appendChild(row);
      }
    }

    addInput() {
      if (!this.loaded || this.saving) {
        return;
      }
      const tag = this.input.value.trim();
      if (tag && !this.currentTags.some((current) => current.toLowerCase() === tag.toLowerCase())) {
        this.currentTags.push(tag);
        this.renderList();
        this.updateButtons();
      }
      this.input.value = "";
      this.input.focus();
    }

    async save() {
      if (this.saving || !this.loaded) {
        return;
      }
      const changes = this.changes();
      if (!changes.add.length && !changes.remove.length) {
        return;
      }
      this.saving = true;
      this.status.textContent = "Saving tags...";
      this.updateButtons();
      try {
        const result = await applyTagChanges(this.context, changes.add, changes.remove);
        this.saving = false;
        if (!result.failures) {
          finishEditing(this.context);
          this.close();
          showToast("Tags saved.");
        } else {
          this.status.textContent = resultMessage(result);
          this.updateButtons();
        }
      } catch (error) {
        this.saving = false;
        this.status.textContent = error.message || "Failed to save tags. Try again.";
        this.updateButtons();
      }
    }

    close() {
      if (this.saving) {
        return;
      }
      document.removeEventListener("keydown", this.onKeydown, true);
      window.removeEventListener("hashchange", this.onNavigation);
      window.removeEventListener("popstate", this.onNavigation);
      this.overlay.remove();
      document.body.classList.remove("jellytag-dialog-open");
      if (this.previousFocus?.isConnected) {
        this.previousFocus.focus();
      }
    }
  }

  function dismissActionSheet(sheet) {
    const dialog = sheet.closest(".actionSheet");
    return new Promise((resolve, reject) => {
      if (!dialog) {
        reject(new Error("Couldn't close the item menu."));
        return;
      }
      const timer = setTimeout(() => reject(new Error("Couldn't close the item menu.")), 2000);
      dialog.addEventListener("close", () => {
        clearTimeout(timer);
        resolve();
      }, { once: true });
      const cancel = dialog.querySelector(".btnCloseActionSheet");
      if (cancel) {
        cancel.click();
      } else if (dialog.dialogContainer) {
        const container = dialog.dialogContainer;
        container.dispatchEvent(new MouseEvent("mousedown", { bubbles: true }));
        container.dispatchEvent(new MouseEvent("click", { bubbles: true }));
      }
    });
  }

  function createMenuButton(iconName, label, onClick) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "listItem listItem-button actionSheetMenuItem emby-button jellytag-menu-button";
    const icon = document.createElement("span");
    icon.className = "actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons " + iconName;
    icon.setAttribute("aria-hidden", "true");
    const body = document.createElement("div");
    body.className = "listItemBody";
    const text = document.createElement("div");
    text.className = "listItemBodyText actionSheetItemText";
    text.textContent = label;
    body.appendChild(text);
    button.append(icon, body);
    button.addEventListener("click", (event) => {
      event.preventDefault();
      event.stopPropagation();
      if (button.disabled) {
        return;
      }
      button.disabled = true;
      Promise.resolve(onClick()).finally(() => {
        button.disabled = false;
      });
    });
    return button;
  }

  async function injectMenuButtons(sheet) {
    if (sheet.hasAttribute(MARKER_ATTR) || processingSheets.has(sheet)) {
      return;
    }
    sheet.setAttribute(MARKER_ATTR, "true");
    const scroller = sheet.querySelector(".actionSheetScroller");
    if (!scroller || !scroller.querySelector('button[data-id="multiSelect"], button[data-id="edit"], button[data-id="addtoplaylist"], button[data-id="playlist"], button[data-id="addtocollection"], button[data-id="editimages"], button[data-id="editsubtitles"], button[data-id="identify"]')) {
      return;
    }
    const context = pendingMenu;
    const session = pendingMenuSession;
    if (menuContextAmbiguous || !isCurrentMenuSession(session)) {
      return;
    }
    if (!repeatedMenuRequest) {
      awaitingMenuEvent = null;
      pendingMenu = null;
      pendingMenuSession = null;
    }
    if (!context) {
      return;
    }
    context.session = session;
    if (!scroller.querySelector('button[data-id="edit"], button[data-id="addtoplaylist"], button[data-id="playlist"], button[data-id="addtocollection"]')) {
      return;
    }
    let closing = false;
    sheet.closest(".actionSheet")?.addEventListener("closing", () => {
      closing = true;
    }, { once: true });
    processingSheets.add(sheet);
    try {
      const apiClient = getApiClient(context);
      const user = await apiClient.getCurrentUser();
      if (!user.Policy?.IsAdministrator) {
        sheet.setAttribute(MARKER_ATTR, "true");
        return;
      }
      const quickTags = await loadQuickTags(apiClient);
      if (!sheet.isConnected || closing || !isCurrentMenuSession(session)) {
        return;
      }
      sheet.setAttribute(MARKER_ATTR, "true");
      const manage = createMenuButton("local_offer", "Manage Tags", async () => {
        try {
          await dismissActionSheet(sheet);
          getApiClient(context);
          await new TagDialog(context).open();
        } catch (error) {
          showToast(error.message || "Failed to load tags.");
        }
      });
      const anchor = scroller.querySelector('button[data-id="addtoplaylist"], button[data-id="playlist"]') || scroller.querySelector('button[data-id="addtocollection"]');
      if (anchor) {
        anchor.after(manage);
      } else {
        scroller.appendChild(manage);
      }
      let last = manage;
      for (const tag of quickTags) {
        const button = createMenuButton("loyalty", "+ " + tag, async () => {
          if (quickTagInFlight) {
            showToast("Tagging in progress...");
            return;
          }
          quickTagInFlight = true;
          try {
            await dismissActionSheet(sheet);
            const result = await applyTagChanges(context, [tag], []);
            if (!result.failures) {
              finishEditing(context);
              showToast("Tagged: " + tag);
            } else {
              showToast(resultMessage(result));
            }
          } catch (error) {
            showToast(error.message || "Failed to add tag: " + tag);
          } finally {
            quickTagInFlight = false;
          }
        });
        last.after(button);
        last = button;
      }
    } catch (error) {
      console.warn("[JellyTag] Could not add tag actions:", error);
    } finally {
      processingSheets.delete(sheet);
    }
  }

  function init() {
    if (document.documentElement.hasAttribute("data-jellytag-client")) {
      return;
    }
    document.documentElement.setAttribute("data-jellytag-client", "true");
    for (const eventName of ["click", "command", "contextmenu", "keydown"]) {
      document.addEventListener(eventName, captureMenuEvent, true);
    }
    window.addEventListener("hashchange", invalidateMenuContext);
    window.addEventListener("popstate", invalidateMenuContext);
    const observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        for (const node of mutation.addedNodes) {
          if (!(node instanceof Element)) {
            continue;
          }
          if (node.matches(".actionSheetContent")) {
            injectMenuButtons(node);
          }
          for (const sheet of node.querySelectorAll(".actionSheetContent")) {
            injectMenuButtons(sheet);
          }
        }
      }
    });
    observer.observe(document.body, { childList: true, subtree: true });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init, { once: true });
  } else {
    init();
  }
})();
