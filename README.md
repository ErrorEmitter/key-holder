# KeyHolder

KeyHolder keeps keyboard keys held down on Windows. It can hold several keys at once, including modifiers such as Shift, Ctrl, Alt and Win. Use it in games to keep moving without holding a key, or anywhere else a key needs to stay down for a long time.

[中文说明](#中文说明)

## Download

Get `KeyHolder.exe` from [Releases](https://github.com/ErrorEmitter/key-holder/releases/latest). It is a single file and needs no installation.

The exe is not code-signed, so Windows SmartScreen may show "Windows protected your PC" the first time you run it. Click "More info", then "Run anyway".

## Requirements

- Windows 10 or 11, x64
- [.NET 11 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/11.0)

## Usage

1. Run `KeyHolder.exe`. It asks for administrator rights (UAC) every time it starts, so it also works with programs that run as administrator.
2. **Hold a key:** press Scroll Lock twice within 1 second, then press the key you want to hold within 1 second. Modifier keys (Shift, Ctrl, Alt, Win) work too.
3. **Hold more keys:** repeat step 2 for each key.
4. **Release one key:** repeat step 2 for a key that is already held. You can also double-click it in the window's list, or select it and click "松开选中" (Release selected).
5. **Release all keys:** press Scroll Lock 4 times in a row, with no more than 1 second between presses. You can also click "全部松开" (Release all).

**Window and tray**

- Closing the window sends the app to the system tray. It keeps working in the background.
- Double-click the tray icon to open the window again.
- Right-click the tray icon for a menu: show the window, turn the main switch on or off, release all keys, or exit.
- The icon color shows the state:

  | Color | Meaning |
  |---|---|
  | Green | Idle |
  | Blue | Waiting for a key |
  | Orange | Keys are held; the number is how many |
  | Gray | The main switch is off |

- All held keys are released automatically when any of these happens:
  - you turn the main switch off
  - you exit the app
  - the session locks
  - a remote desktop session disconnects
  - the PC goes to sleep

**Interface labels** (the interface is in Chinese)

| Label | Meaning |
|---|---|
| 总开关：已开启 / 已关闭 | Main switch: on / off. Click it to toggle. |
| 已关闭：不响应触发 | Switched off: triggers are ignored |
| 空闲 | Idle |
| 等待按键… | Waiting for the key to hold |
| 再按一下 Scroll Lock 全部松开 | Press Scroll Lock once more to release all keys |
| 正在按住 N 个键 | N keys are held |
| 上次操作 | Last action |
| 松开选中 | Release the selected key |
| 全部松开 | Release all keys |
| 退出程序 | Exit |

## Behavior details

- **Scroll Lock is only the trigger.**
  - It can never be held.
  - Its presses are not blocked, so after an even number of presses the Scroll Lock light is back where it started.
- **Counting Scroll Lock presses:**
  - Presses count as one sequence as long as each one comes within 1 second of the previous one.
  - Pressing any other key starts the count over.
  - After 3 presses, KeyHolder does not wait for a key. A fourth Scroll Lock press releases all keys; any other key is typed normally.
- **Pressing a held key yourself:** it stays held. It is released only by the trigger sequence or from the window.
- **No auto-repeat:** a held key is sent as a single "key down" event, and Windows does not auto-repeat simulated keys. Holding A in a text box types only one "a"; games treat the key as held down.
- **Separate keys:** left and right Shift, Ctrl, Alt and Win are different keys, and so are numpad Enter and the main Enter.
- **Scan codes:** each simulated key carries both its virtual-key code and its scan code, so games that read only scan codes receive it too.
- **Win+L:** if you press Win+L while keys are held, KeyHolder releases them all before locking the session, so nothing stays pressed on the lock screen.
- **Single instance:** only one copy runs at a time. Starting it again brings up the window of the copy that is already running.
- **Limits:**
  - Some games with anti-cheat ignore simulated input; KeyHolder does not work in them.
  - If KeyHolder is killed (for example from Task Manager), held keys may stay stuck. Press the stuck key once to release it.

## How it works

A global low-level keyboard hook (`WH_KEYBOARD_LL`) watches for the trigger sequence. `SendInput` then sends only the "key down" event of the chosen key, without the matching "key up". The key press that picks the key is blocked, so the foreground program never sees it. The main logic is in [KeyHoldEngine.cs](KeyHoldEngine.cs).

## Build

Requires the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0).

```powershell
dotnet publish -c Release -o dist
```

`dist\KeyHolder.exe` is a single file that depends on the .NET 11 Desktop Runtime installed on the machine.

## Test

```powershell
powershell -ExecutionPolicy Bypass -File tests\Run-Tests.ps1
```

The script:

1. builds a test copy that does not ask for administrator rights
2. simulates key presses with `SendInput`, using only F21–F24, right Shift and Scroll Lock
3. checks the resulting key states
4. restores the Scroll Lock state at the end

Do not touch the keyboard while it runs.

## License

[CC0 1.0](LICENSE). This project is dedicated to the public domain: anyone may copy, modify, distribute and use it, including commercially, without asking permission or giving credit.

---

## 中文说明

KeyHolder 让键盘上的键保持按下状态，可以同时按住多个键，包括 Shift、Ctrl、Alt、Win 这类修饰键。在游戏里可以不用一直按着键就持续移动，其他需要长时间按住某个键的场合也能用。

### 下载

从 [Releases](https://github.com/ErrorEmitter/key-holder/releases/latest) 下载 `KeyHolder.exe`。单个文件，不用安装。

exe 没有数字签名，第一次运行时 Windows SmartScreen 可能提示"Windows 已保护你的电脑"。点"更多信息"，再点"仍要运行"即可。

### 运行要求

- Windows 10 / 11，x64
- [.NET 11 桌面运行时](https://dotnet.microsoft.com/download/dotnet/11.0)（Windows Desktop Runtime）

### 用法

1. 运行 `KeyHolder.exe`。程序每次启动都要求管理员权限（会弹 UAC），因此对以管理员身份运行的程序也有效。
2. **按住一个键：** 1 秒内按两下 Scroll Lock，再在 1 秒内按下要按住的键。Shift、Ctrl、Alt、Win 也可以。
3. **同时按住更多键：** 对每个键都做一遍第 2 步。
4. **松开一个键：** 对已经按住的键再做一遍第 2 步。也可以在窗口列表里双击它，或者选中后点"松开选中"。
5. **松开全部：** 连按 4 下 Scroll Lock（相邻两下间隔不超过 1 秒），或者点"全部松开"。

**窗口和托盘**

- 关闭窗口时程序缩到托盘，后台照常工作。
- 双击托盘图标可以重新打开窗口。
- 右键托盘图标有菜单：显示主窗口、总开关、全部松开、退出。
- 托盘图标的颜色：
  - 绿色：空闲
  - 蓝色：正在等待按键
  - 橙色：有键被按住，图标上的数字是按住的键数
  - 灰色：总开关已关闭
- 下面任何一种情况发生时，所有按住的键都会自动松开：
  - 关闭总开关
  - 退出程序
  - 锁屏
  - 断开远程桌面
  - 电脑睡眠

### 行为细节

- **Scroll Lock 只用来触发：**
  - 本身不会被按住。
  - 按下时也不会被拦截，所以连按偶数下，指示灯会回到原来的状态。
- **Scroll Lock 连按怎么计数：**
  - 相邻两下间隔不超过 1 秒，就算同一串连按。
  - 中间按了别的键，就从头计数。
  - 连按 3 下后不会进入等待：再按一下 Scroll Lock 就是全部松开，按别的键则正常输入。
- **亲手按放已经按住的键：** 按住状态不会断。只有重复触发，或者在窗口里操作，才会松开。
- **不会自动连发：** 模拟的按键只有一次"按下"，Windows 不会让它自动连发。在文本框里按住 A 只会出一个 a；在游戏里，这个键会一直被当作按着。
- **左右键分开：** 左右 Shift、Ctrl、Alt、Win 算不同的键，小键盘 Enter 和主键盘 Enter 也算不同的键。
- **扫描码：** 发送按键时同时带虚拟键码和扫描码，只认扫描码的游戏也能收到。
- **Win+L：** 有键被按住时按 Win+L，程序会先松开所有键再锁屏，锁屏界面上不会有键还按着。
- **单实例：** 同一时间只能运行一个。再启动一次，会把已经在运行的窗口叫出来。
- **限制：**
  - 有些带反作弊的游戏会忽略模拟输入，在这些游戏里本程序无效。
  - 如果程序被强制结束（比如在任务管理器里结束），按住的键可能卡住，亲手按一下那个键就能恢复。

### 原理

程序用全局低级键盘钩子（`WH_KEYBOARD_LL`）识别触发组合，再用 `SendInput` 只发送所选按键的"按下"，不发送对应的"抬起"。用来选键的那次按键会被拦截，前台程序收不到。主要逻辑在 [KeyHoldEngine.cs](KeyHoldEngine.cs)。

### 编译

需要 [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)。

```powershell
dotnet publish -c Release -o dist
```

生成的 `dist\KeyHolder.exe` 是单个文件，依赖本机装好的 .NET 11 桌面运行时。

### 测试

```powershell
powershell -ExecutionPolicy Bypass -File tests\Run-Tests.ps1
```

脚本会做这几件事：

1. 编译一份不要求管理员的测试版
2. 用 `SendInput` 模拟按键，只用 F21–F24、右 Shift 和 Scroll Lock
3. 检查按键状态
4. 结束时恢复 Scroll Lock 的切换状态

运行期间不要碰键盘。

### 许可证

[CC0 1.0](LICENSE)：本项目放弃全部版权，进入公有领域。任何人都可以复制、修改、发布、商用，不需要署名，也不需要征得同意。
