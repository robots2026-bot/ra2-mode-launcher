# RA2 Mode Launcher

一个面向本机《尤里的复仇》安装的独立 RA2 经典模式启动器原型。

- 底层运行 `gamemd-spawn.exe`，不是纯 RA2 的 `game.exe`。
- 可切换 CnCNet Spawner 的 RA2 经典规则和尤里复仇规则。
- 支持遭遇战地图、国家、颜色、队伍、AI 难度、固定出生点、全图可见和 `.SAV` 存档加载。
- 游戏速度保留引擎原生的 `GameSpeed=0～6`，并用 cnc-ddraw 的 `maxgameticks` 在“很快”和“不限速”之间提供 75、90、120 三个高速档位。
- 新建对局会给超时空军团兵的普通、精英与特殊武器使用独立弹道，使其能够隔墙冷冻目标，并将射程分别设为 8、9、10 格，同时不改变其他 `InvisibleLow` 武器。
- 参战位置按照地图容量固定显示，可逐个设置为玩家、电脑、开放或关闭；存档列表优先显示游戏内填写的存档名称。
- 新游戏加载 Ares、CnCNet Spawner 和 Phobos；读档时检查存档摘要，启动器生成的 Ares/Phobos 存档使用相同扩展组合读取，旧式无扩展存档仍只加载 CnCNet Spawner。
- 日常运行、地图扫描、存档、配置和维护都只访问当前游戏目录 `D:\Software\RA2Mode`，不依赖旧安装目录。
- 局域网第一版支持 UDP 房间发现、按 IP 加入、TCP 房间状态与地图同步、准备状态，以及由 CnCNet Spawner 使用 UDP 1234 启动真人对战；启动前会校验地图及关键 EXE/DLL/MIX 指纹，不包含互联网大厅或公网中继。
- 局域网窗口显示发现、房间和游戏端口，并优先显示实际局域网 IPv4；不会把回环地址 `127.0.0.1` 当作默认房主地址。

## 构建

项目源码单独由 Git 管理。构建时使用上级目录 `..\vendor\package_9.3.3` 中的 CnCNet 9.3.3 依赖；体积较大的 `vendor` 内容及其独立上游仓库不纳入本仓库。

```powershell
dotnet build .\Ra2ModeLauncher.csproj -c Release
```

发布和刷新当前游戏目录：

```powershell
dotnet publish .\Ra2ModeLauncher.csproj -c Release --self-contained false
.\deploy-runtime.ps1
```

当前目录需要预先包含合法的游戏资源（例如 `ra2.mix`）。部署脚本只使用当前目录与项目内的官方 CnCNet 9.3.3 组件，不再访问旧安装目录；同时给 `gamemd-spawn.exe` 应用已经验证过的“驻军建筑被超时空移除”崩溃修复。原始 Spawner 会保留为 `gamemd-spawn.exe.official-9.3.3`。

## 当前边界

当前处理本地遭遇战（一个真人和 1-7 个 AI）与本地 `.SAV` 存档，暂不处理局域网和地图预览图。
