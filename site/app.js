const platformNote = document.querySelector("#platformNote");

if (platformNote && !/Windows/i.test(navigator.userAgent)) {
  platformNote.classList.add("is-warning");
  platformNote.querySelector("span:last-child").textContent =
    "当前系统不是 Windows。安装包仅支持 Windows 10/11 x64，请在 Windows 电脑上下载运行。";
}
