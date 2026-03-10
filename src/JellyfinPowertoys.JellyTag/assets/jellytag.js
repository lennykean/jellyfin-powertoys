(function () {
  "use strict";

  const PLUGIN_ID = "36eb87e0-0373-423b-a547-2acb96e33430";
  const MARKER_ATTR = "data-jellytag-injected";

  let quickTags = [];
  let lastMenuItemId = null;

  function getCredentials() {
    const storageItem = localStorage.getItem("jellyfin_credentials");
    if (!storageItem) {
      return null;
    }
    let credentials;
    try {
      credentials = JSON.parse(storageItem);
    } catch (e) {
      console.warn("[JellyTag] Failed to parse credentials from localStorage:", e);
      return null;
    }
    for (const server of credentials?.Servers ?? []) {
      if (![server.ManualAddress, server.LocalAddress, server.RemoteAddress].includes(location.origin)) {
        continue;
      }
      return {
        serverUrl: location.origin,
        apiKey: server.AccessToken,
      };
    }
    return null;
  }

  async function loadQuickTags() {
    try {
      const credentials = getCredentials();
      let config = null;
      if (credentials) {
        const response = await fetch(`${credentials.serverUrl}/Plugins/${PLUGIN_ID}/Configuration`, {
          headers: {
            Authorization: `MediaBrowser Token="${credentials.apiKey}"`,
          },
        });
        if (response.ok) {
          config = await response.json();
        }
      }
      quickTags = (config && config.QuickTags) || [];
    } catch (e) {
      console.warn("[JellyTag] Failed to load plugin config:", e);
      quickTags = [];
    }
  }

  function extractIdFromHref(href) {
    if (!href) {
      return null;
    }
    const match = href.match(/[?&]id=([a-f0-9]+)/i);
    return match ? match[1] : null;
  }

  function getSelectedItemIds() {
    const ids = [];

    const checkboxes = document.querySelectorAll(".itemSelectionPanel input[type='checkbox']:checked, .chkItemSelect:checked, .itemSelectCheckbox:checked");
    for (const cb of checkboxes) {
      const card = cb.closest("[data-id]");
      if (card) {
        const id = card.getAttribute("data-id");
        if (id && !ids.includes(id)) {
          ids.push(id);
        }
        continue;
      }
      const container = cb.closest(".card, .listItem, .cardBox");
      if (container) {
        const link = container.querySelector('a[data-action="link"], a[href*="id="]');
        const extractedId = extractIdFromHref(link && link.getAttribute("href"));
        if (extractedId && !ids.includes(extractedId)) {
          ids.push(extractedId);
        }
      }
    }

    return ids;
  }

  function getSingleItemId() {
    const hash = window.location.hash || "";
    const urlId = extractIdFromHref(hash);
    if (urlId) {
      return urlId;
    }

    if (lastMenuItemId) {
      return lastMenuItemId;
    }

    return null;
  }

  function cleanItemForUpdate(item) {
    const copy = JSON.parse(JSON.stringify(item));
    const fieldsToStrip = [
      "Trickplay",
      "TrickplayInfo",
      "PlayAccess",
      "People",
      "Studios",
      "GenreItems",
      "TagItems",
      "ArtistItems",
      "AlbumArtists",
      "MediaStreams",
      "MediaSources",
      "Chapters",
      "RemoteTrailers",
      "ImageTags",
      "BackdropImageTags",
      "ParentBackdropImageTags",
    ];
    for (const field of fieldsToStrip) {
      delete copy[field];
    }
    return copy;
  }

  async function applyTagChanges(itemIds, tagsToAdd, tagsToRemove) {
    const userId = ApiClient.getCurrentUserId();
    const lowerRemove = tagsToRemove.map((t) => t.toLowerCase());
    let successes = 0;
    let failures = 0;
    let forbidden = false;

    async function processItem(id) {
      try {
        const item = await ApiClient.getItem(userId, id);
        item.Tags = item.Tags || [];
        for (const tag of tagsToAdd) {
          if (!item.Tags.some((t) => t.toLowerCase() === tag.toLowerCase())) {
            item.Tags.push(tag);
          }
        }
        item.Tags = item.Tags.filter((t) => !lowerRemove.includes(t.toLowerCase()));
        await ApiClient.updateItem(cleanItemForUpdate(item));
        successes++;
      } catch (e) {
        if (e && (e.status === 403 || (e.response && e.response.status === 403))) {
          forbidden = true;
        }
        console.error("[JellyTag] Failed to update item " + id + ":", e);
        failures++;
      }
    }

    for (let i = 0; i < itemIds.length; i += 5) {
      const batch = itemIds.slice(i, i + 5);
      await Promise.allSettled(batch.map((id) => processItem(id)));
    }

    if (forbidden) {
      return { successes, failures, forbidden: true };
    }
    return { successes, failures, forbidden: false };
  }

  async function addTagToItems(itemIds, tagName) {
    const result = await applyTagChanges(itemIds, [tagName], []);
    if (result.forbidden) {
      throw Object.assign(new Error("You don't have permission to edit tags."), { forbidden: true });
    }
    return result;
  }

  async function getTagInfo(itemIds) {
    if (itemIds.length === 0) {
      return { tags: [], counts: new Map() };
    }
    const userId = ApiClient.getCurrentUserId();
    const BATCH_SIZE = 5;
    const allResults = [];
    for (let i = 0; i < itemIds.length; i += BATCH_SIZE) {
      const batch = itemIds.slice(i, i + BATCH_SIZE);
      const results = await Promise.allSettled(batch.map((id) => ApiClient.getItem(userId, id)));
      allResults.push(...results);
    }
    const tagMap = new Map();
    for (const result of allResults) {
      if (result.status !== "fulfilled") {
        continue;
      }
      const tags = result.value.Tags || [];
      for (const tag of tags) {
        const lower = tag.toLowerCase();
        if (tagMap.has(lower)) {
          tagMap.get(lower).count++;
        } else {
          tagMap.set(lower, { name: tag, count: 1 });
        }
      }
    }
    const tags = [];
    const counts = new Map();
    for (const { name, count } of tagMap.values()) {
      tags.push(name);
      counts.set(name.toLowerCase(), count);
    }
    return { tags, counts };
  }

  function exitSelectionMode() {
    const cancelBtn = document.querySelector(".btnCloseSelectionPanel");
    if (cancelBtn) {
      cancelBtn.click();
    }
  }

  function showToast(message) {
    if (typeof require !== "undefined") {
      try {
        require(["toast"], function (toast) {
          toast(message);
        });
        return;
      } catch {}
    }
    if (typeof Dashboard !== "undefined" && Dashboard.alert) {
      Dashboard.alert(message);
    }
  }

  class TagDialog {
    constructor(itemIds) {
      this.itemIds = itemIds;
      this.totalItems = itemIds.length;
      this.originalTags = [];
      this.currentTags = [];
      this.tagCounts = new Map();
      this.promotedTags = new Set();
      this.overlay = null;
      this.dialog = null;
      this.input = null;
      this.listEl = null;
      this.saveBtn = null;
      this.resetBtn = null;
      this.addBtn = null;
    }

    async open() {
      const info = await getTagInfo(this.itemIds);
      this.originalTags = [...info.tags];
      this.currentTags = [...info.tags];
      this.tagCounts = info.counts;

      this._buildDOM();
      document.body.appendChild(this.overlay);

      setTimeout(() => this.input.focus(), 100);
    }

    _hasPendingChanges() {
      if (this.promotedTags.size > 0) {
        return true;
      }
      if (this.currentTags.length !== this.originalTags.length) {
        return true;
      }
      const sorted1 = [...this.currentTags].sort();
      const sorted2 = [...this.originalTags].sort();
      return sorted1.some((t, i) => t !== sorted2[i]);
    }

    _buildDOM() {
      this.overlay = document.createElement("div");
      this.overlay.className = "dialogContainer";
      this.overlay.addEventListener("mousedown", (e) => {
        if (e.target === this.overlay) {
          this.close();
        }
      });

      this.dialog = document.createElement("div");
      this.dialog.className = "focuscontainer dialog dialog-fixedSize dialog-small formDialog opened jellytag-dialog";

      // Header
      const header = document.createElement("div");
      header.className = "formDialogHeader";

      const closeBtn = document.createElement("button");
      closeBtn.type = "button";
      closeBtn.setAttribute("is", "paper-icon-button-light");
      closeBtn.className = "btnCancel autoSize paper-icon-button-light";
      closeBtn.innerHTML = '<span class="material-icons arrow_back" aria-hidden="true"></span>';
      closeBtn.addEventListener("click", () => this.close());

      const title = document.createElement("h3");
      title.className = "formDialogHeaderTitle";
      title.textContent = "Manage Tags";

      header.appendChild(closeBtn);
      header.appendChild(title);

      const content = document.createElement("div");
      content.className = "formDialogContent";

      const contentInner = document.createElement("div");
      contentInner.className = "dialogContentInner";

      const inputRow = document.createElement("div");
      inputRow.className = "jellytag-input-row";

      const inputContainer = document.createElement("div");
      inputContainer.className = "inputContainer";

      this.input = document.createElement("input");
      this.input.setAttribute("is", "emby-input");
      this.input.type = "text";
      this.input.className = "emby-input";
      this.input.setAttribute("label", "Tag");
      this.input.addEventListener("keydown", (e) => {
        if (e.key === "Enter") {
          e.preventDefault();
          this._addCurrentInput();
        }
      });

      const inputLabel = document.createElement("label");
      inputLabel.className = "inputLabel";
      inputLabel.textContent = "Tag";

      inputContainer.appendChild(inputLabel);
      inputContainer.appendChild(this.input);

      this.addBtn = document.createElement("button");
      this.addBtn.type = "button";
      this.addBtn.setAttribute("is", "emby-button");
      this.addBtn.className = "fab btnAddTextItem submit marginStart emby-button";
      this.addBtn.title = "Add";
      this.addBtn.innerHTML = '<span class="material-icons add" aria-hidden="true"></span>';
      this.addBtn.addEventListener("click", () => this._addCurrentInput());

      inputRow.appendChild(inputContainer);
      inputRow.appendChild(this.addBtn);
      contentInner.appendChild(inputRow);

      this.listEl = document.createElement("div");
      this.listEl.className = "paperList";
      contentInner.appendChild(this.listEl);

      content.appendChild(contentInner);

      const footer = document.createElement("div");
      footer.className = "formDialogFooter";

      const cancelBtn = document.createElement("button");
      cancelBtn.type = "button";
      cancelBtn.setAttribute("is", "emby-button");
      cancelBtn.className = "raised button-cancel block btnCancel formDialogFooterItem emby-button";
      cancelBtn.textContent = "Cancel";
      cancelBtn.addEventListener("click", () => this.close());

      this.resetBtn = document.createElement("button");
      this.resetBtn.type = "button";
      this.resetBtn.setAttribute("is", "emby-button");
      this.resetBtn.className = "raised button-reset block btnReset formDialogFooterItem emby-button";
      this.resetBtn.textContent = "Reset";
      this.resetBtn.addEventListener("click", () => this._reset());

      this.saveBtn = document.createElement("button");
      this.saveBtn.type = "button";
      this.saveBtn.setAttribute("is", "emby-button");
      this.saveBtn.className = "raised button-submit block btnSave formDialogFooterItem emby-button";
      this.saveBtn.textContent = "Save";
      this.saveBtn.addEventListener("click", () => this._save());

      footer.appendChild(cancelBtn);
      footer.appendChild(this.resetBtn);
      footer.appendChild(this.saveBtn);

      this.dialog.appendChild(header);
      this.dialog.appendChild(content);
      this.dialog.appendChild(footer);
      this.overlay.appendChild(this.dialog);

      this._renderList();
      this._updateButtons();

      this._escHandler = (e) => {
        if (e.key === "Escape") {
          this.close();
        }
      };
      document.addEventListener("keydown", this._escHandler);
    }

    _updateButtons() {
      const hasChanges = this._hasPendingChanges();
      if (this.saveBtn) {
        this.saveBtn.disabled = !hasChanges;
      }
      if (this.resetBtn) {
        this.resetBtn.disabled = !hasChanges;
      }
    }

    _reset() {
      this.currentTags = [...this.originalTags];
      this.promotedTags.clear();
      this._renderList();
      this._updateButtons();
    }

    _isPartial(tag) {
      if (this.totalItems <= 1) {
        return false;
      }
      if (this.promotedTags.has(tag.toLowerCase())) {
        return false;
      }
      if (!this.originalTags.some((t) => t.toLowerCase() === tag.toLowerCase())) {
        return false;
      }
      return (this.tagCounts.get(tag.toLowerCase()) || 0) < this.totalItems;
    }

    _renderList() {
      this.listEl.innerHTML = "";
      if (this.currentTags.length === 0) {
        const emptyMsg = document.createElement("div");
        emptyMsg.className = "jellytag-empty";
        emptyMsg.textContent = "No tags.";
        this.listEl.appendChild(emptyMsg);
        return;
      }
      const sorted = [...this.currentTags].sort((a, b) => a.localeCompare(b));
      for (const tag of sorted) {
        const item = document.createElement("div");
        item.className = "listItem";
        const partial = this._isPartial(tag);

        const icon = document.createElement("span");
        icon.className = "material-icons listItemIcon local_offer";
        icon.setAttribute("aria-hidden", "true");

        const promoted = this.totalItems > 1 && this.promotedTags.has(tag.toLowerCase());

        if (partial) {
          const count = this.tagCounts.get(tag.toLowerCase()) || 0;
          icon.style.backgroundColor = "rgba(255,255,255,0.1)";
          icon.style.opacity = "0.4";
          icon.style.cursor = "pointer";
          icon.title = "Applied to " + count + " of " + this.totalItems + " items";
          icon.addEventListener("click", () => this._promoteTag(tag));
        } else if (promoted) {
          icon.style.backgroundColor = "rgba(255,255,255,0.1)";
          icon.style.cursor = "pointer";
          icon.title = "Tag will be applied to all items";
          icon.addEventListener("click", () => this._demoteTag(tag));
        } else {
          icon.style.backgroundColor = "transparent";
        }

        const body = document.createElement("div");
        body.className = "listItemBody";
        const textDiv = document.createElement("div");
        textDiv.className = "textValue";
        textDiv.textContent = tag;
        body.appendChild(textDiv);

        const removeBtn = document.createElement("button");
        removeBtn.type = "button";
        removeBtn.setAttribute("is", "paper-icon-button-light");
        removeBtn.className = "btnRemoveFromEditorList autoSize paper-icon-button-light";
        removeBtn.innerHTML = '<span class="material-icons delete" aria-hidden="true"></span>';
        removeBtn.addEventListener("click", () => this._removeTag(tag));

        item.appendChild(icon);
        item.appendChild(body);
        item.appendChild(removeBtn);
        this.listEl.appendChild(item);
      }
    }

    _addCurrentInput() {
      const val = this.input.value.trim();
      if (!val) {
        return;
      }
      if (!this.currentTags.some((t) => t.toLowerCase() === val.toLowerCase())) {
        this.currentTags.push(val);
        this._renderList();
        this._updateButtons();
      }
      this.input.value = "";
      this.input.focus();
    }

    _promoteTag(tagName) {
      this.promotedTags.add(tagName.toLowerCase());
      this._renderList();
      this._updateButtons();
    }

    _demoteTag(tagName) {
      this.promotedTags.delete(tagName.toLowerCase());
      this._renderList();
      this._updateButtons();
    }

    _removeTag(tagName) {
      this.currentTags = this.currentTags.filter((t) => t.toLowerCase() !== tagName.toLowerCase());
      this.promotedTags.delete(tagName.toLowerCase());
      this._renderList();
      this._updateButtons();
    }

    async _save() {
      if (this._saving) {
        return;
      }
      this._saving = true;
      const tagsToAdd = this.currentTags.filter((t) => {
        const isNew = !this.originalTags.some((o) => o.toLowerCase() === t.toLowerCase());
        const isPromoted = this.promotedTags.has(t.toLowerCase());
        return isNew || isPromoted;
      });
      const tagsToRemove = this.originalTags.filter((t) => !this.currentTags.some((c) => c.toLowerCase() === t.toLowerCase()));

      if (tagsToAdd.length === 0 && tagsToRemove.length === 0) {
        this._saving = false;
        this.close();
        return;
      }

      this.saveBtn.disabled = true;
      this.saveBtn.textContent = "Saving...";

      try {
        const result = await applyTagChanges(this.itemIds, tagsToAdd, tagsToRemove);
        if (result.forbidden) {
          showToast("You don't have permission to edit tags.");
        } else if (result.failures === 0) {
          showToast("Tags saved.");
        } else if (result.successes === 0) {
          showToast("Failed to save tags.");
        } else {
          showToast("Saved tags for " + result.successes + " of " + (result.successes + result.failures) + " items. " + result.failures + " failed.");
        }
        this._saving = false;
        if (result.failures > 0 && result.successes === 0) {
          if (this.saveBtn) {
            this.saveBtn.disabled = false;
            this.saveBtn.textContent = "Save";
          }
        } else {
          this.close();
          exitSelectionMode();
        }
      } catch (e) {
        console.error("[JellyTag] Failed to save tags:", e);
        showToast("Failed to save tags.");
        this._saving = false;
        if (this.saveBtn) {
          this.saveBtn.disabled = false;
          this.saveBtn.textContent = "Save";
        }
      }
    }

    close() {
      if (this._saving) {
        return;
      }
      if (this._escHandler) {
        document.removeEventListener("keydown", this._escHandler);
      }
      if (this.overlay && this.overlay.parentNode) {
        this.overlay.parentNode.removeChild(this.overlay);
      }
      this.overlay = null;
    }
  }

  function createMenuButton(iconName, label, onClick) {
    const btn = document.createElement("button");
    btn.setAttribute("is", "paper-icon-button-light");
    btn.className = "btnOption listItem listItem-button actionSheetMenuItem emby-button";

    const icon = document.createElement("span");
    icon.className = "actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons " + iconName;

    const text = document.createElement("span");
    text.className = "actionSheetItemText";
    text.textContent = label;

    btn.appendChild(icon);
    btn.appendChild(text);

    btn.addEventListener("click", (e) => {
      e.preventDefault();
      e.stopPropagation();
      onClick();
    });

    return btn;
  }

  function dismissActionSheet() {
    const actionSheet = document.querySelector(".actionSheet");
    if (!actionSheet) {
      return;
    }

    const cancelBtn = actionSheet.querySelector(".btnCloseActionSheet, .btnCancel");
    if (cancelBtn) {
      cancelBtn.click();
      return;
    }

    if (window.history.length > 1) {
      window.history.back();
    }
  }

  let quickTagInFlight = false;

  async function injectMenuButtons(actionSheet) {
    if (actionSheet.hasAttribute(MARKER_ATTR)) {
      return;
    }

    isMutatingDOM = true;
    try {
      actionSheet.setAttribute(MARKER_ATTR, "true");

      const capturedSingleId = getSingleItemId();

      await loadQuickTags();

      const buttons = actionSheet.querySelectorAll("button");
      let anchorButton = null;

      for (const btn of buttons) {
        const textEl = btn.querySelector(".actionSheetItemText");
        if (textEl) {
          const text = textEl.textContent.trim().toLowerCase();
          if (text === "add to playlist" || text === "add to collection") {
            if (text === "add to playlist") {
              anchorButton = btn;
              break;
            }
            if (!anchorButton) {
              anchorButton = btn;
            }
          }
        }
      }

      const insertAfter = anchorButton || null;

      const tagDialogBtn = createMenuButton("local_offer", "Manage Tags", () => {
        const multiIds = getSelectedItemIds();
        const itemIds = multiIds.length > 0 ? multiIds : capturedSingleId ? [capturedSingleId] : [];
        dismissActionSheet();
        if (itemIds.length === 0) {
          showToast("No items found to tag.");
          return;
        }
        const dialog = new TagDialog(itemIds);
        dialog.open().catch((e) => {
          console.error("[JellyTag] Failed to open tag dialog:", e);
          showToast("Failed to load tags.");
        });
      });

      const container = insertAfter ? insertAfter.parentNode : actionSheet;
      if (insertAfter && insertAfter.nextSibling) {
        container.insertBefore(tagDialogBtn, insertAfter.nextSibling);
      } else if (insertAfter) {
        container.appendChild(tagDialogBtn);
      } else {
        container.appendChild(tagDialogBtn);
      }

      let lastInserted = tagDialogBtn;
      for (const tag of quickTags) {
        const quickBtn = createMenuButton("loyalty", "+ " + tag, () => {
          if (quickTagInFlight) {
            showToast("Tagging in progress...");
            return;
          }
          quickTagInFlight = true;
          const multiIds = getSelectedItemIds();
          const itemIds = multiIds.length > 0 ? multiIds : capturedSingleId ? [capturedSingleId] : [];
          dismissActionSheet();
          if (itemIds.length === 0) {
            quickTagInFlight = false;
            showToast("No items found to tag.");
            return;
          }
          addTagToItems(itemIds, tag)
            .then(() => {
              showToast("Tagged: " + tag);
              exitSelectionMode();
            })
            .catch((e) => {
              console.error("[JellyTag] Quick tag failed:", e);
              if (e && e.forbidden) {
                showToast("You don't have permission to edit tags.");
              } else {
                showToast("Failed to add tag: " + tag);
              }
            })
            .finally(() => {
              quickTagInFlight = false;
            });
        });

        if (lastInserted.nextSibling) {
          lastInserted.parentNode.insertBefore(quickBtn, lastInserted.nextSibling);
        } else {
          lastInserted.parentNode.appendChild(quickBtn);
        }
        lastInserted = quickBtn;
      }
    } finally {
      isMutatingDOM = false;
    }
  }

  function tryInjectIntoActionSheets() {
    const sheets = document.querySelectorAll(".actionSheet");
    for (const sheet of sheets) {
      injectMenuButtons(sheet);
    }
  }

  let observer = null;
  let debounceTimer = null;
  let isMutatingDOM = false;

  function startObserver() {
    if (observer) {
      return;
    }

    observer = new MutationObserver(() => {
      if (isMutatingDOM) {
        return;
      }
      clearTimeout(debounceTimer);
      debounceTimer = setTimeout(() => {
        debounceTimer = null;
        tryInjectIntoActionSheets();
      }, 50);
    });

    observer.observe(document.body, {
      childList: true,
      subtree: true,
    });
  }

  let initRetries = 0;
  const MAX_INIT_RETRIES = 30;

  async function init() {
    if (typeof ApiClient === "undefined") {
      if (++initRetries > MAX_INIT_RETRIES) {
        console.warn("[JellyTag] ApiClient not available after " + MAX_INIT_RETRIES + " retries, giving up.");
        return;
      }
      setTimeout(init, 1000);
      return;
    }

    document.addEventListener(
      "click",
      (e) => {
        const menuBtn = e.target.closest('[data-action="menu"]');
        if (menuBtn) {
          const card = menuBtn.closest("[data-id]");
          lastMenuItemId = card ? card.getAttribute("data-id") : null;
        }
      },
      true,
    );

    startObserver();

    console.log("[JellyTag] Initialized.");
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
