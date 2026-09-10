const $ = (selector) => document.querySelector(selector);

const elements = {
  clearBtn: $("#clearBtn"),
  modeCopyBtn: $("#modeCopyBtn"),
  modeReplaceBtn: $("#modeReplaceBtn"),
  modeNote: $("#modeNote"),
  sourceTitle: $("#sourceTitle"),
  sourceCount: $("#sourceCount"),
  uploadZone: $("#uploadZone"),
  folderZone: $("#folderZone"),
  fileInput: $("#fileInput"),
  pickFilesBtn: $("#pickFilesBtn"),
  pickFolderBtn: $("#pickFolderBtn"),
  folderHint: $("#folderHint"),
  sourceSummary: $("#sourceSummary"),
  summaryText: $("#summaryText"),
  formulaInput: $("#formulaInput"),
  presetList: $("#presetList"),
  dateSource: $("#dateSource"),
  dateFormat: $("#dateFormat"),
  startNum: $("#startNum"),
  padNum: $("#padNum"),
  stripCheck: $("#stripCheck"),
  lowerCheck: $("#lowerCheck"),
  overwriteCheck: $("#overwriteCheck"),
  changedOnlyCheck: $("#changedOnlyCheck"),
  sortSelect: $("#sortSelect"),
  previewCount: $("#previewCount"),
  fileBody: $("#fileBody"),
  emptyState: $("#emptyState"),
  applyBtn: $("#applyBtn"),
  applyBtnText: $("#applyBtnText"),
  applyMessage: $("#applyMessage"),
  resetRuleBtn: $("#resetRuleBtn"),
  toast: $("#toast"),
};

const state = {
  mode: "copy",
  items: [],
  folderRoots: [],
  busy: false,
  nextId: 1,
  toastTimer: null,
  rule: null,
};

const supportsFsAccess = "showDirectoryPicker" in window;
const FORBIDDEN_BASE = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;
const FORBIDDEN_CHARS = /[<>:"/\\|?*\u0000-\u001f]/g;

function formatDate(date, format) {
  const p = (value) => String(value).padStart(2, "0");
  const tokens = {
    YYYY: String(date.getFullYear()),
    MM: p(date.getMonth() + 1),
    DD: p(date.getDate()),
    HH: p(date.getHours()),
    mm: p(date.getMinutes()),
    ss: p(date.getSeconds()),
  };
  return format.replace(/YYYY|MM|DD|HH|mm|ss/g, (token) => tokens[token]);
}

function formatShortDate(timestamp) {
  const date = new Date(timestamp);
  return `${formatDate(date, "YYYY-MM-DD")} ${formatDate(date, "HH:mm")}`;
}

function splitName(filename) {
  const lastDot = filename.lastIndexOf(".");
  if (lastDot > 0 && lastDot < filename.length - 1) {
    return [filename.slice(0, lastDot), filename.slice(lastDot + 1)];
  }
  return [filename, ""];
}

function sanitizeBase(base) {
  let value = String(base)
    .replace(FORBIDDEN_CHARS, "-")
    .replace(/[. ]+$/g, "")
    .replace(/^[. ]+/g, "")
    .trim();
  if (!value) return "file";
  if (FORBIDDEN_BASE.test(value)) return `_${value}`;
  return value;
}

function cleanOriginalName(filename) {
  const [base] = splitName(filename);
  let value = base;
  const cleanCheck = elements.stripCheck.checked;
  if (cleanCheck) {
    value = value
      .replace(/\s*\((\d+)\)\s*$/g, "")
      .replace(/\s*\[(\d+)\]\s*$/g, "")
      .replace(/\s*-\s*副本\s*$/g, "")
      .replace(/\s*副本\s*$/g, "")
      .replace(/\s*\(副本\)\s*$/g, "");
  }
  if (elements.lowerCheck.checked) value = value.toLowerCase();
  return value;
}

function getRule() {
  const formula = elements.formulaInput.value.trim();
  const pad = Number(elements.padNum.value);
  const rule = {
    formula: formula || "{date}_{num}",
    dateSource: elements.dateSource.value,
    dateFormat: elements.dateFormat.value,
    start: Math.max(0, Number(elements.startNum.value) || 0),
    pad: pad > 0 ? pad : 0,
    strip: elements.stripCheck.checked,
    lower: elements.lowerCheck.checked,
    autoSuffix: elements.overwriteCheck.checked,
  };
  state.rule = rule;
  return rule;
}

function getOrderedItems() {
  const mode = elements.sortSelect.value;
  const items = [...state.items];
  if (mode === "name") {
    items.sort((a, b) =>
      a.currentName.localeCompare(b.currentName, "zh-Hans-CN", {
        numeric: true,
        sensitivity: "base",
      })
    );
  } else if (mode === "modified") {
    items.sort((a, b) => a.lastModified - b.lastModified || a.currentName.localeCompare(b.currentName));
  } else {
    items.sort((a, b) => a.addedIndex - b.addedIndex);
  }
  return items;
}

function buildPlan() {
  const rule = getRule();
  const ordered = getOrderedItems();
  const now = new Date();
  const plan = new Map();
  const usedTargets = new Map(); // groupKey -> Set
  const originalNames = new Map(); // groupKey -> Map<name, itemId>

  ordered.forEach((item) => {
    const groupKey = state.mode === "replace" ? item.dirKey : "__download__";
    if (!usedTargets.has(groupKey)) usedTargets.set(groupKey, new Set());
    if (!originalNames.has(groupKey)) originalNames.set(groupKey, new Map());

    const used = usedTargets.get(groupKey);
    const originals = originalNames.get(groupKey);
    if (!originals.has(item.currentName)) {
      originals.set(item.currentName, item.id);
    }

    const [originalBase, extension] = splitName(item.originalName);
    const cleanBase = cleanOriginalName(item.originalName) || originalBase || "file";
    const dateValue =
      rule.dateSource === "now" ? now : new Date(item.lastModified);
    const formattedDate = formatDate(dateValue, rule.dateFormat);
    const numberValue = rule.start + ordered.indexOf(item);
    const paddedNumber = String(numberValue).padStart(rule.pad, "0");
    const pad2 = (value) => String(value).padStart(2, "0");
    const templateValues = {
      date: formattedDate,
      time: `${pad2(dateValue.getHours())}-${pad2(dateValue.getMinutes())}-${pad2(dateValue.getSeconds())}`,
      num: paddedNumber,
      name: cleanBase,
      YYYY: String(dateValue.getFullYear()),
      M: String(dateValue.getMonth() + 1),
      MM: pad2(dateValue.getMonth() + 1),
      D: String(dateValue.getDate()),
      DD: pad2(dateValue.getDate()),
      H: String(dateValue.getHours()),
      HH: pad2(dateValue.getHours()),
      m: String(dateValue.getMinutes()),
      mm: pad2(dateValue.getMinutes()),
      s: String(dateValue.getSeconds()),
      ss: pad2(dateValue.getSeconds()),
    };
    const substituted = rule.formula
      .replace(/\{\{/g, "\uE000")
      .replace(/\}\}/g, "\uE001")
      .replace(/\{([^{}]+)\}/g, (match, key) =>
        Object.prototype.hasOwnProperty.call(templateValues, key)
          ? templateValues[key]
          : match
      )
      .replace(/\uE000/g, "{")
      .replace(/\uE001/g, "}");
    const sanitized = sanitizeBase(substituted);
    let targetBase = sanitized;
    let targetName = extension ? `${sanitized}.${extension}` : sanitized;
    let blocked = false;

    if (state.mode === "replace") {
      const existingOwner = originals.get(targetName);
      if (existingOwner && existingOwner !== item.id && rule.autoSuffix) {
        let suffixIndex = 2;
        while (
          used.has(targetName) ||
          (originals.has(targetName) && originals.get(targetName) !== item.id)
        ) {
          targetBase = `${sanitized}_${suffixIndex}`;
          targetName = extension ? `${targetBase}.${extension}` : targetBase;
          suffixIndex += 1;
        }
      } else if (existingOwner && existingOwner !== item.id && !rule.autoSuffix) {
        blocked = true;
      }
    }

    while (used.has(targetName)) {
      const match = targetBase.match(/^(.*)_(\d+)$/);
      const stem = match ? match[1] : targetBase;
      const next = (match ? Number(match[2]) + 1 : 2);
      targetBase = `${stem}_${next}`;
      targetName = extension ? `${targetBase}.${extension}` : targetBase;
    }

    used.add(targetName);
    plan.set(item.id, {
      targetName,
      blocked,
      changed: targetName !== item.currentName,
      item,
    });
  });

  return plan;
}

function render() {
  const plan = buildPlan();
  const rule = state.rule || getRule();
  const ordered = getOrderedItems();
  const showChangedOnly = elements.changedOnlyCheck.checked;
  const visibleRows = ordered.filter((item) => {
    const entry = plan.get(item.id);
    return !showChangedOnly || entry.changed;
  });
  const sourceCount = state.items.length;
  const changeCount = [...plan.values()].filter((entry) => entry.changed).length;

  elements.previewCount.textContent = String(visibleRows.length);
  elements.sourceCount.textContent = String(sourceCount);
  elements.fileBody.innerHTML = "";

  if (sourceCount === 0) {
    elements.emptyState.hidden = false;
    elements.applyBtn.disabled = true;
    elements.applyBtnText.textContent =
      state.mode === "replace" ? "替换源文件" : "下载改名文件";
    elements.clearBtn.disabled = true;
    return;
  }

  elements.emptyState.hidden = true;
  elements.applyBtn.disabled = false;
  elements.clearBtn.disabled = false;
  elements.applyBtnText.textContent =
    state.mode === "replace" ? "一键替换源文件" : "一键下载改名文件";

  visibleRows.forEach((item, index) => {
    const entry = plan.get(item.id);
    const extension = splitName(item.originalName)[1];
    const extensionText = extension ? `.${extension}` : "无扩展名";
    const timeText =
      rule.dateSource === "now"
        ? formatShortDate(Date.now())
        : formatShortDate(item.lastModified);
    let stateLabel = "待处理";
    let stateClass = "pending";

    if (entry.blocked) {
      stateLabel = "需处理";
      stateClass = "warning";
    } else if (entry.targetName === item.currentName) {
      stateLabel = item.renamed ? "已改名" : "保持不变";
      stateClass = item.renamed ? "done" : "same";
    } else if (item.renamed) {
      stateLabel = "再次改名";
    }

    const row = document.createElement("tr");
    row.className = item.renamed ? "is-done" : "";
    row.dataset.id = item.id;

    const pathHtml = item.displayPath
      ? `<div class="file-path" title="${escapeHtml(item.displayPath)}">${escapeHtml(item.displayPath)}</div>`
      : "";
    const sameTarget = entry.targetName === item.currentName;

    row.innerHTML = `
      <td><span class="row-index">${index + 1}</span></td>
      <td>
        <div class="file-cell">
          <div class="file-main">
            <span class="file-extension">${escapeHtml(extensionText)}</span>
            <div class="file-min">
              <div class="file-name" title="${escapeHtml(item.originalName)}">${escapeHtml(item.originalName)}</div>
              ${pathHtml}
            </div>
          </div>
        </div>
      </td>
      <td><span class="time-cell">${timeText}</span></td>
      <td>
        <div class="target-cell">
          <span class="target-name${sameTarget ? " is-same" : ""}" title="${escapeHtml(entry.targetName)}">${escapeHtml(entry.targetName)}</span>
        </div>
      </td>
      <td><span class="state-chip ${stateClass}">${stateLabel}</span></td>
    `;
    elements.fileBody.appendChild(row);
  });

  if (visibleRows.length === 0) {
    elements.emptyState.hidden = false;
    elements.emptyState.querySelector("p").textContent = "没有待改名的文件";
  }

  const hasResult =
    elements.applyMessage.classList.contains("is-success") ||
    elements.applyMessage.classList.contains("is-error");
  if (!hasResult) {
    if (changeCount === 0 && sourceCount > 0) {
      elements.applyMessage.textContent = state.mode === "replace" ? "文件无需改名" : "没有需要下载的新文件";
    } else if (changeCount > 0) {
      elements.applyMessage.textContent = `预计生成 ${changeCount} 个新文件名`;
    }
  }
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

function showToast(message, type = "info") {
  clearTimeout(state.toastTimer);
  elements.toast.textContent = message;
  elements.toast.classList.toggle("is-error", type === "error");
  elements.toast.classList.add("is-visible");
  state.toastTimer = setTimeout(() => {
    elements.toast.classList.remove("is-visible");
  }, 2800);
}

function setBusy(busy) {
  state.busy = busy;
  elements.applyBtn.disabled = busy || state.items.length === 0;
  elements.applyBtn.querySelector("svg").style.display = busy ? "none" : "";
  elements.applyBtnText.textContent = busy
    ? "处理中..."
    : state.mode === "replace"
      ? "一键替换源文件"
      : "一键下载改名文件";
}

function addUploadFiles(files) {
  const added = [];
  for (const file of files) {
    const id = state.nextId++;
    added.push({
      id,
      addedIndex: state.items.length + added.length,
      sourceType: "copy",
      originalName: file.name,
      currentName: file.name,
      file,
      size: file.size,
      lastModified: file.lastModified,
      displayPath: "",
      dirKey: "",
      renamed: false,
    });
  }
  state.items.push(...added);
  clearResultMessage();
  updateSummary();
  render();
}

async function scanDirectory(rootHandle) {
  const entries = [];

  async function walk(directoryHandle, relativePath = "") {
    for await (const entry of directoryHandle.values()) {
      if (entry.kind === "directory") {
        if (entry.name.startsWith(".")) continue;
        const nextPath = relativePath ? `${relativePath}/${entry.name}` : entry.name;
        await walk(entry, nextPath);
      } else if (entry.kind === "file") {
        const file = await entry.getFile();
        entries.push({
          handle: entry,
          parent: directoryHandle,
          file,
          relativePath,
        });
      }
    }
  }

  await walk(rootHandle);
  return entries;
}

function addFolderEntries(rootHandle, entries) {
  const dirKey = `disk:${state.nextId++}`;
  const offset = state.items.length;
  const added = entries
    .map((entry) => {
      const id = state.nextId++;
      return {
        id,
        addedIndex: state.items.length + state.items.length + id,
        sourceType: "replace",
        originalName: entry.file.name,
        currentName: entry.file.name,
        handle: entry.handle,
        parentHandle: entry.parent,
        file: entry.file,
        size: entry.file.size,
        lastModified: entry.file.lastModified,
        displayPath: entry.relativePath ? `${entry.relativePath}/${entry.file.name}` : entry.file.name,
        dirKey,
        renamed: false,
      };
    })
    .sort((a, b) => a.currentName.localeCompare(b.currentName));

  added.forEach((item, index) => {
    item.addedIndex = offset + index;
  });
  state.items.push(...added);
  state.folderRoots.push({
    name: rootHandle.name || "文件夹",
    handle: rootHandle,
    count: entries.length,
  });
  clearResultMessage();
}

async function pickFolder() {
  if (!supportsFsAccess) {
    showToast("当前浏览器不支持直接选择文件夹，请使用 Chrome 或 Edge", "error");
    return;
  }
  try {
    const rootHandle = await window.showDirectoryPicker({ mode: "readwrite" });
    elements.folderHint.textContent = "正在读取文件列表...";
    const entries = await scanDirectory(rootHandle);
    if (entries.length === 0) {
      showToast("该文件夹内没有可处理的文件", "error");
      elements.folderHint.textContent = "";
      return;
    }
    addFolderEntries(rootHandle, entries);
    elements.folderHint.textContent = "";
    updateSummary();
    render();
    showToast(`已读取 ${entries.length} 个文件`);
  } catch (error) {
    if (error && error.name === "AbortError") return;
    elements.folderHint.textContent = "";
    showToast(error?.message || "读取文件夹失败", "error");
  }
}

function updateSummary() {
  const count = state.items.length;
  if (count === 0) {
    elements.sourceSummary.hidden = true;
    return;
  }
  elements.sourceSummary.hidden = false;
  if (state.mode === "replace") {
    const rootText = state.folderRoots.map((root) => `"${root.name}"`).join("、");
    elements.summaryText.textContent = rootText
      ? `来自 ${rootText}，共 ${count} 个文件`
      : `已添加 ${count} 个文件`;
  } else {
    elements.summaryText.textContent = `已添加 ${count} 个文件`;
  }
}

function clearResultMessage() {
  elements.applyMessage.textContent = "";
  elements.applyMessage.classList.remove("is-error", "is-success");
}

function clearItems() {
  state.items = [];
  state.folderRoots = [];
  elements.sourceSummary.hidden = true;
  clearResultMessage();
  render();
}

function switchMode(mode) {
  if (state.mode === mode) return;
  if (state.items.length > 0) {
    clearItems();
  }
  state.mode = mode;
  elements.modeCopyBtn.classList.toggle("is-active", mode === "copy");
  elements.modeReplaceBtn.classList.toggle("is-active", mode === "replace");
  elements.modeCopyBtn.setAttribute("aria-selected", String(mode === "copy"));
  elements.modeReplaceBtn.setAttribute("aria-selected", String(mode === "replace"));
  elements.uploadZone.hidden = mode !== "copy";
  elements.folderZone.hidden = mode !== "replace";

  if (mode === "replace") {
    elements.modeNote.textContent = supportsFsAccess
      ? "直接在所选文件夹内改名，文件数据不经过网页复制。"
      : "当前浏览器不支持直接替换源文件，请使用 Chrome 或 Edge。";
    elements.pickFolderBtn.disabled = !supportsFsAccess;
    elements.sourceTitle.textContent = "文件夹来源";
  } else {
    elements.modeNote.textContent = "生成改名副本，不修改电脑上的原文件。";
    elements.sourceTitle.textContent = "文件来源";
  }
  updateSummary();
  render();
}

async function downloadRenamed(entries) {
  let success = 0;
  for (const entry of entries) {
    const targetName = entry.targetName;
    const originalFile = entry.item.file;
    try {
      const renamedFile = new File([originalFile], targetName, {
        type: originalFile.type || "application/octet-stream",
        lastModified: originalFile.lastModified,
      });
      const url = URL.createObjectURL(renamedFile);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = targetName;
      anchor.rel = "noopener";
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      setTimeout(() => URL.revokeObjectURL(url), 3000);
      success += 1;
      await new Promise((resolve) => setTimeout(resolve, 160));
    } catch (error) {
      showToast(`${targetName} 下载失败`, "error");
    }
  }
  return success;
}

async function replaceSources(entries) {
  let success = 0;
  const failures = [];
  for (const entry of entries) {
    const item = entry.item;
    const targetName = entry.targetName;
    if (!item.handle || !item.parentHandle) {
      failures.push(item.originalName);
      continue;
    }
    try {
      if (typeof item.handle.move === "function") {
        await item.handle.move(targetName);
      } else {
        const sourceFile = await item.handle.getFile();
        const destination = await item.parentHandle.getFileHandle(targetName, {
          create: true,
        });
        const writable = await destination.createWritable();
        await writable.write(sourceFile);
        await writable.close();
        if (typeof item.handle.remove === "function") {
          await item.handle.remove();
        } else {
          throw new Error("当前浏览器不支持移动文件");
        }
      }
      item.currentName = targetName;
      item.renamed = true;
      success += 1;
    } catch (error) {
      failures.push(item.originalName);
    }
  }
  return { success, failures };
}

async function handleApply() {
  if (state.busy) return;
  const plan = buildPlan();
  const entries = [...plan.values()].filter(
    (entry) => entry.changed && !entry.blocked
  );

  if (entries.length === 0) {
    const blockedCount = [...plan.values()].filter((entry) => entry.blocked).length;
    if (blockedCount > 0) {
      showToast("存在需要手动处理的同名冲突", "error");
    } else {
      showToast(state.mode === "replace" ? "文件无需改名" : "没有需要下载的文件");
    }
    return;
  }

  setBusy(true);
  elements.applyMessage.textContent = "";
  elements.applyMessage.classList.remove("is-error", "is-success");
  try {
    if (state.mode === "replace") {
      const result = await replaceSources(entries);
      if (result.success > 0) {
        showToast(`已直接替换 ${result.success} 个源文件`);
        elements.applyMessage.textContent = `已完成：${result.success} 个源文件已原地改名`;
        elements.applyMessage.classList.add("is-success");
      }
      if (result.failures.length > 0) {
        const message = `${result.failures.length} 个文件失败`;
        elements.applyMessage.textContent = message;
        elements.applyMessage.classList.add("is-error");
        showToast(message, "error");
      }
    } else {
      const success = await downloadRenamed(entries);
      elements.applyMessage.textContent = `已开始下载 ${success} 个改名文件`;
      elements.applyMessage.classList.add("is-success");
      if (success > 0) showToast(`已开始下载 ${success} 个改名文件`);
    }
  } finally {
    setBusy(false);
    render();
  }
}

function insertToken(token) {
  const input = elements.formulaInput;
  const start = input.selectionStart ?? input.value.length;
  const end = input.selectionEnd ?? input.value.length;
  input.value = input.value.slice(0, start) + token + input.value.slice(end);
  const cursor = start + token.length;
  input.focus();
  input.setSelectionRange(cursor, cursor);
  syncPresetState();
  render();
}

function syncPresetState() {
  const value = elements.formulaInput.value.trim();
  elements.presetList.querySelectorAll(".preset").forEach((button) => {
    button.classList.toggle("is-active", button.dataset.preset === value);
  });
}

function resetRule() {
  elements.formulaInput.value = "{date}_{num}";
  elements.dateSource.value = "modified";
  elements.dateFormat.value = "YYYY-MM-DD_HH-mm-ss";
  elements.startNum.value = "1";
  elements.padNum.value = "2";
  elements.stripCheck.checked = false;
  elements.lowerCheck.checked = false;
  elements.overwriteCheck.checked = true;
  syncPresetState();
  clearResultMessage();
  render();
}

function bindEvents() {
  elements.modeCopyBtn.addEventListener("click", () => switchMode("copy"));
  elements.modeReplaceBtn.addEventListener("click", () => switchMode("replace"));
  elements.pickFilesBtn.addEventListener("click", () => elements.fileInput.click());
  elements.pickFolderBtn.addEventListener("click", pickFolder);
  elements.fileInput.addEventListener("change", () => {
    addUploadFiles(elements.fileInput.files);
    elements.fileInput.value = "";
  });

  elements.clearBtn.addEventListener("click", clearItems);
  elements.resetRuleBtn.addEventListener("click", resetRule);
  elements.applyBtn.addEventListener("click", handleApply);

  elements.formulaInput.addEventListener("input", () => {
    syncPresetState();
    clearResultMessage();
    render();
  });
  elements.presetList.querySelectorAll(".preset").forEach((button) => {
    button.addEventListener("click", () => {
      elements.formulaInput.value = button.dataset.preset;
      syncPresetState();
      clearResultMessage();
      render();
    });
  });
  document.querySelectorAll(".token-btn").forEach((button) => {
    button.addEventListener("click", () => {
      insertToken(button.dataset.token);
      clearResultMessage();
    });
  });

  [elements.dateSource, elements.dateFormat, elements.startNum, elements.padNum].forEach(
    (control) => control.addEventListener("input", () => {
      clearResultMessage();
      render();
    })
  );
  [elements.stripCheck, elements.lowerCheck, elements.overwriteCheck].forEach(
    (control) => control.addEventListener("change", () => {
      clearResultMessage();
      render();
    })
  );
  elements.changedOnlyCheck.addEventListener("change", () => {
    clearResultMessage();
    render();
  });
  elements.sortSelect.addEventListener("change", () => {
    clearResultMessage();
    render();
  });

  elements.uploadZone.addEventListener("dragover", (event) => {
    event.preventDefault();
    elements.uploadZone.classList.add("is-dragging");
  });
  elements.uploadZone.addEventListener("dragleave", () => {
    elements.uploadZone.classList.remove("is-dragging");
  });
  elements.uploadZone.addEventListener("drop", (event) => {
    event.preventDefault();
    elements.uploadZone.classList.remove("is-dragging");
    if (event.dataTransfer?.files?.length) {
      addUploadFiles(event.dataTransfer.files);
    }
  });

  document.addEventListener("keydown", (event) => {
    if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
      event.preventDefault();
      handleApply();
    }
  });
}

function init() {
  elements.overwriteCheck.checked = true;
  elements.pickFolderBtn.disabled = !supportsFsAccess;
  if (!supportsFsAccess) {
    elements.folderHint.textContent = "当前浏览器不支持直接替换源文件";
  }
  bindEvents();
  switchMode("copy");
  syncPresetState();
  render();
}

init();
