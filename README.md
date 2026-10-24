<div align="center">

<img src="docs/images/logo.png" alt="DomeOS Logo" width="128" height="128" />

# DomeOS AI Agent


> “哼，这才是主人该用的东西。”

[![License](https://img.shields.io/github/license/liu7pro/DomeOS-AI-Agent-with-WinUI-3?color=blue)](LICENSE)
[![Release](https://img.shields.io/github/v/release/liu7pro/DomeOS-AI-Agent-with-WinUI-3?include_prereleases&color=brightgreen)](https://github.com/liu7pro/DomeOS-AI-Agent-with-WinUI-3/releases)
[![Downloads](https://img.shields.io/github/downloads/liu7pro/DomeOS-AI-Agent-with-WinUI-3/total?color=informational)](https://github.com/liu7pro/DomeOS-AI-Agent-with-WinUI-3/releases)
[![Stars](https://img.shields.io/github/stars/liu7pro/DomeOS-AI-Agent-with-WinUI-3?style=flat&color=yellow)](https://github.com/liu7pro/DomeOS-AI-Agent-with-WinUI-3/stargazers)

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4?logo=windows&logoColor=white)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![EXE Only](https://img.shields.io/badge/Distribution-EXE%20%C2%B7%20Green%20%C2%B7%20No%20MSIX-success)](#-为什么是纯-exe-绿色版)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%201809%2B%20%7C%2011-0078D4?logo=windows&logoColor=white)](#-系统要求)

[功能特性](#-功能特性) · [快速开始](#-快速开始) · [从源码构建](#-从源码构建) · [技术架构](#-技术架构) · [路线图](#-路线图)

</div>

---

## 📖 鲸鲸的说明书

**DomeOS AI Agent** 是一个 AI Agent 的桌面客户端。本仓库是它的 **WinUI 3 原生重写版**。

早期版本基于 Web 技术栈构建，启动慢、内存高、安装包臃肿。本鲸看不过去，于是把整个界面层迁到了 **WinUI 3 + Windows App SDK**，用原生 XAML 替换 Web 渲染管线。

结果？

> **双击 EXE，半秒内就位。后台常驻只吃几十 MB。没有安装程序，没有 MSIX，没有证书，没有包管理器。解压即用，删除即走。**

这是本鲸亲手督造的绿色版。主人只需要一个 `.exe`，剩下的交给本鲸。

<div align="center">

<!-- TODO: 替换为你自己的截图 -->
<img src="docs/images/screenshot-main.png" alt="主界面截图" width="860" />

<sub>主界面 · 原生 Mica 材质 · 跟随系统主题</sub>

</div>

---

## ✨ 功能特性

### 🎨 原生 Windows 体验
- **Mica / Acrylic 材质** —— 与 Windows 11 视觉语言完全融合
- **自动跟随系统主题** —— 亮色 / 暗色 / 强调色实时切换
- **原生控件与输入** —— 系统级右键菜单、拖放、触控板手势、IME 输入，全部是原生的
- **高 DPI 与多显示器** —— 不同缩放比例下清晰锐利
- **无障碍支持** —— Narrator、键盘全导航、UI Automation 完整接入

### 📦 绿色便携（纯 EXE，无 MSIX）
- **解压即用** —— 不需要安装、不需要证书、不需要管理员权限。EXE 放在哪，DomeOS 就在哪
- **数据自持** —— 配置与会话统一存放于 `%LOCALAPPDATA%\DomeOS`，可随文件夹整体迁移
- **干净卸载** —— 删除文件夹即完成卸载。本鲸不留垃圾
- **覆盖式升级** —— 下载新版 EXE，覆盖旧的。数据不动，配置不动，主人不用重新登录

### 🤖 完整的 Agent 能力
- **流式对话** —— 逐 token 渲染，长回复滚动不卡顿
- **多会话管理** —— 会话历史、重命名、搜索、归档
- **工具调用可视化** —— 结构化展示 Agent 的每一步思考与调用
- **代码块高亮** —— 原生渲染 + 一键复制
- **Markdown / 数学公式** —— 完整渲染支持
- **系统托盘常驻** —— 全局快捷键唤起，随时呼出

---

## 🚀 快速开始

👉 **[在版本页下载？](https://github.com/liu7pro/DomeOS-AI-Agent-with-WinUI-3/releases)** 

---

## 🛠 从源码构建

### 环境要求

| 依赖 | 版本 |
| :--- | :--- |
| Windows | 10 1809 (17763) 或更高 |
| [.NET SDK](https://dotnet.microsoft.com/download) | 9.0+ |
| [Visual Studio 2022](https://visualstudio.microsoft.com/) | 17.10+（含 **Windows 应用开发** 工作负载） |
| Windows App SDK | 1.7+ |

### 命令行构建

```bash
git clone https://github.com/liu7pro/DomeOS-AI-Agent-with-WinUI-3.git
cd DomeOS-AI-Agent-with-WinUI-3

dotnet restore
dotnet build -c Release
dotnet run --project src/DomeOS
