# 文件改名台 / 视频截图拼图台

两个 Windows 原生桌面工具，界面直接运行在 exe 中，不打开浏览器、不依赖 Node/Python。

在线展示页：<https://tenaciousMa.github.io/local-tools-suite/>

## 视频截图拼图台安装包

标准安装包：

```text
dist/installer/视频截图拼图台_安装包_v1.0.0.exe
```

安装包支持：

- 自定义安装目录
- 开始菜单快捷方式
- 可选桌面快捷方式
- 可选安装完成后启动
- 标准卸载程序
- 简体中文安装向导

完整工程构建：

```powershell
.\build.ps1 -Version 1.0.0
```

只构建绿色版应用：

```powershell
.\build-app.ps1 -Version 1.0.0
```

详细工程结构见 `docs/PROJECT_STRUCTURE.md`。

支持：

- 添加文件或整个文件夹，也能直接把文件拖进窗口
- 在预览表里看到每个文件的原名与改名后文件名
- 直接改源文件模式：一键修改本地原文件名，不使用复制副本
- 改名成功后文件保留在列表中，可用“一键清空”手动清空整个工作区
- 导出 ZIP 模式：把改名后的文件打包成 zip，不改原文件；保存对话框默认打开原文件夹，可手动选择其他保存位置
- 可开关的“改名后打开原文件夹”按钮：开启后，改名或导出完成会自动打开源文件夹
- 命名模板可自由输入文字，并插入日期、时间、序号、原名等变量

## 直接运行

双击 `dist/文件改名台.exe` 即可。exe 为 .NET 8 WPF self-contained 单文件发布，所有运行环境都封装在文件内。

## 命名模板

模板中可直接输入任意文字，再通过按钮或手动输入插入变量：

- `{date}`：按“日期格式”生成完整日期/时间
- `{YYYY}年{MM}月{DD}日`、`{YYYY}-{MM}-{DD}`：自由日期
- `{HH}时{mm}分`：自由时间
- `{num}`：自动序号
- `{name}`：清理后的原文件名

## 重新打包 exe

```powershell
dotnet publish .\desktop\RenamerDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\desktop\publish
Copy-Item .\desktop\publish\FileRenamerDesktop.exe .\dist\文件改名台.exe -Force
```

应用图标由 `tools/generate_icon.py` 生成，产物为 `assets/app-icon.ico`。
视频截图工具图标由 `tools/generate_video_icon.py` 生成，产物为 `assets/video-icon.ico`。

## 视频截图拼图台

批量把视频按时长等分成指定段数，截取每段中间帧后合成一张拼图大图。提供 2、4、6、8、9、12、16、20、24 张常用排布，也可自由输入任意张数；支持大标题、每格小标题和可自定义时间格式。

功能：

- 添加多个视频或整个文件夹，也能直接拖入窗口
- 内置基于 LibVLC 的常见播放器界面：播放/暂停、前后 10 秒、拖动进度条，把当前时间点加入手动截图
- 播放器快捷键：空格/K 播放暂停、←/→ 5 秒、J/L 10 秒、↑/↓ 音量、M 静音、F 全屏、Home/End 首尾、数字键 0-9 按百分比跳转
- “播放器选帧”已并入每页格数选项，可和 2、4、5、6、8、9 等排布直接切换
- 短视频一次批量解码多帧；长视频自动切换关键帧附近快速跳转，不再从开头一路解码到结尾
- 自动按时长等分取帧，或使用播放器手动选帧，每格默认显示对应时间
- 单视频总截图数超过每页格数时自动分多页导出
- 多个视频可单独导出、按顺序合并，或两种都做
- 大标题可用 `{name}`、`{date}`、`{num}`、`{duration}` 等变量，也可直接写文字
- 画格比例可跟随视频原比例，也可统一为 16:9、4:3 或 1:1
- 大图尺寸支持自动、1080P、2K、4K、8K、16K、A3、A4 横竖版
- PNG / JPG / PDF 可选；PDF 会按排版分页
- PDF 可选择“单个视频分别生成”“所有视频合并成一个 PDF”或“两种都生成”
- 导出命名默认使用 `{name}_截图`，多页时自动追加 `_p01`
- 命名公式沿用文件改名台的日期、时间、序号、原名等变量，多页时自动追加 `_p01`、`_p02`
- 预览单条视频后即可批量导出全部拼图

运行：

```text
dist/视频截图拼图台/视频截图拼图台.exe
```

绿色版必须完整保留 `dist/视频截图拼图台` 目录。目录中包含 `.NET` 运行库、LibVLC 播放器和 FFmpeg，不要只复制单个 exe。

重新打包：

```powershell
dotnet publish .\desktop\video\VideoGridDesktop.csproj -c Release -r win-x64 --self-contained true -o .\dist\视频截图拼图台
Copy-Item .\dist\视频截图拼图台\VideoGridDesktop.exe .\dist\视频截图拼图台\视频截图拼图台.exe -Force
Remove-Item .\dist\视频截图拼图台\VideoGridDesktop.exe -Force
```
