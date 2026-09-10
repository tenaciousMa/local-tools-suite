# 工程结构

## 应用层

- `desktop/video/`：视频截图拼图台的 WPF 源码
- `desktop/`：原文件改名台源码
- `assets/`：应用图标和资源
- `tools/`：图标生成等辅助脚本

## 构建层

- `build-app.ps1`：只发布视频工具应用本体
- `build.ps1`：发布应用并生成安装包
- `installer/build-installer.ps1`：安装包构建入口
- `installer/bootstrap-inno.ps1`：缺少 Inno Setup 时自动安装在当前用户目录
- `installer/VideoGridSetup.iss`：安装器定义
- `installer/languages/ChineseSimplified.isl`：简体中文安装向导

## 输出层

- `dist/视频截图拼图台/`：绿色版应用目录
- `dist/installer/视频截图拼图台_安装包_v1.0.0.exe`：安装包

## 构建命令

```powershell
.\build.ps1 -Version 1.0.0
```

如果只需要绿色版应用：

```powershell
.\build-app.ps1 -Version 1.0.0
```

安装包包含：

- 安装路径选择页
- 开始菜单快捷方式
- 可选桌面快捷方式
- 安装完成后启动选项
- 标准卸载程序
