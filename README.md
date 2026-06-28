# 键盘按键映射工具（Windows Key Mapper · C#/WPF）

一个 Windows 原生桌面小工具，通过底层键盘钩子把任意「源键」改写为「目标键」，并提供
图形界面进行可视化配置。适合键盘缺键（如缺 Home/End）时用其它键顶替。

- **原生精简**：C#/.NET 8 + WPF，原生 `WH_KEYBOARD_LL` 钩子、原生 `NotifyIcon` 托盘，
  无 Python 运行时、无第三方 UI 库；可发布为单文件 exe。
- **现代界面**：自绘深色主题、圆角卡片、强调色按钮、状态指示灯。
- **系统托盘**：关闭窗口即最小化到托盘后台运行，右键托盘图标退出。

---

## ⚠️ 关于 Fn 键（务必先读）

**Fn 键几乎无法用软件改写。** 绝大多数键盘/笔记本的 Fn 由键盘固件/嵌入式控制器拦截，
**不产生能到达 Windows 的扫描码**，所以本工具、AutoHotkey 这类软件层工具都「看不见」它，
无法映射成 End。在界面里点「捕获按键」后按 Fn，通常不会有任何反应——这就是该限制的体现。

- ✅ **右 Ctrl → Home**：软件可见键，**完全可以**映射，添加后即生效。
- ❌ **Fn → End**：软件层基本做不到。替代方案：
  - 进 BIOS/UEFI 设置（很多笔记本支持 Fn 锁、或 Fn/Ctrl 互换）；
  - 用厂商工具（联想 Vantage、Dell Optimizer、HP Command Center 等）；
  - 换一个把 Fn 暴露为扫描码的键盘。

本工具对「Windows 能看见的键」是通用的：源键支持按下捕获，目标键从可搜索列表选择。

---

## 构建与运行

需在 **Windows** 上构建。安装
[.NET 8 SDK](https://dotnet.microsoft.com/download)（含桌面运行时）。

```bat
cd KeyMapper
dotnet run -c Release
```

### 发布为单文件 exe

框架依赖（体积极小，约 1 MB，需目标机装有 .NET 8 桌面运行时）：

```bat
cd KeyMapper
dotnet publish -c Release -o publish
:: 产物：publish\KeyMapper.exe
```

自包含（无需装运行时，体积较大，约 70 MB+）：

```bat
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

首次运行为空配置，按需添加映射；配置写入 `%APPDATA%\KeyMapper\config.json`。

---

## 使用说明

1. **添加映射**：「＋ 添加」→ 源键处点「捕获按键」后按下想改的物理键（如右 Ctrl）→
   在目标键列表中搜索并选择要变成的键（如 Home）→ 确定。
2. **启用/禁用**：勾选行首复选框，或选中一行点「⇄ 启用/禁用」/ 双击该行。仅打勾的行生效。
3. **编辑/删除**：选中一行后点对应按钮。
4. **启动/停止改键**：点「● 启动改键」生效，「■ 停止改键」撤销。运行中修改配置会自动热更新。
5. **最小化到托盘**：关闭窗口（✕）不会退出，而是收起到右下角托盘；左键双击托盘图标恢复窗口，
   **右键托盘图标 → 退出** 可完全退出。
6. **保存配置**：配置在每次变更时自动写入 `%APPDATA%\KeyMapper\config.json`，
   「保存配置」可手动保存并查看路径。

### 权限说明

底层钩子（`WH_KEYBOARD_LL`）普通使用**无需管理员权限**。但若要让改键在**已提权（UAC）的
窗口**（如任务管理器、部分安装程序）里也生效，需以**管理员身份**运行本程序。

---

## 项目结构

```
KeyMapper/
├── KeyMapper.csproj        .NET 8 WPF 工程
├── App.xaml(.cs)           应用入口、资源合并、ShutdownMode
├── Themes/Theme.xaml        深色主题（画刷、按钮、卡片、列表、文本框样式）
├── Core/
│   ├── Keys.cs              按键目录（Id / 显示名 / 虚拟键码 / 是否扩展键）
│   ├── Config.cs            Mapping/AppConfig + JSON 存取
│   ├── KeyboardHook.cs      WH_KEYBOARD_LL 钩子：改键 + 按键捕获
│   └── TrayIcon.cs          WinForms NotifyIcon 托盘 + 代码绘制图标
├── MappingVm.cs             映射的界面绑定模型（INotifyPropertyChanged）
├── MainWindow.xaml(.cs)     主界面：列表、增删改、启停、托盘集成
└── MappingDialog.xaml(.cs)  添加/编辑对话框：源键捕获 + 目标键搜索
```

## 限制

- Fn 等纯硬件键无法改写（见上）。
- 极少数带反作弊的全屏游戏可能屏蔽软件层钩子。
- 源键被改写后，其原有功能（如右 Ctrl 作修饰键）在该映射生效期间不可用——这是改键的预期行为。
