using System.ComponentModel;
using System.Runtime.InteropServices;
using static KeyHolder.Native;

namespace KeyHolder;

/// <summary>一个物理按键：虚拟键码 + 扫描码 + 是否扩展键（区分左右 Ctrl、小键盘 Enter 等）。</summary>
public readonly record struct KeyId(ushort Vk, ushort Scan, bool Extended)
{
    public string Name => KeyNames.Get(this);
}

public sealed record EngineSnapshot(
    bool Enabled,
    bool Armed,
    int ArmRemainingMs,
    bool ReleaseAllPending,
    IReadOnlyList<KeyId> Held,
    string? LastAction);

/// <summary>
/// 全局低级键盘钩子 + 状态机：
/// Scroll Lock 连按（相邻两下间隔不超过 1 秒）计数：
///   连按 2 下进入"等待"，之后 1 秒内按下的键被切换（未按住→按住，已按住→松开）；
///   连按 4 下松开所有键。
/// Scroll Lock 只用来触发，本身不会被按住。
/// 按住 = 只注入"按下"，不注入"抬起"。
/// </summary>
public sealed unsafe class KeyHoldEngine : IDisposable
{
    public const uint ScrollGapMs = 1000;
    public const uint ArmWindowMs = 1000;
    public const int ReleaseAllPresses = 4;

    // 标记本程序自己注入的事件，钩子看到它直接放行
    const nuint InjectedTag = 0x4B48_4C44; // "KHLD"
    const uint WM_APP_LOCK = WM_APP + 1;

    static KeyHoldEngine? s_current;

    readonly Lock _gate = new();
    readonly List<KeyId> _held = [];
    // 触发键按下时被拦截，它后续的自动重复和抬起也要一起拦掉
    readonly HashSet<KeyId> _swallowUntilUp = [];
    readonly List<KeyId> _releasedOnLock = [];

    bool _enabled = true;
    bool _scrollDown;
    int _scrollCount;       // 当前这一串 Scroll Lock 连按了几下
    uint _lastScrollTime;   // 最近一下 Scroll Lock 的时间（毫秒 tick）
    string? _lastAction;

    Thread? _thread;
    uint _threadId;
    nint _hook;
    bool _disposed;

    /// <summary>状态变化时触发；可能在钩子线程上调用。</summary>
    public event Action? Changed;

    public void Start()
    {
        using var ready = new ManualResetEventSlim();
        Exception? error = null;

        _thread = new Thread(() =>
        {
            MSG msg;
            // 先建好消息队列，PostThreadMessage 才能用
            PeekMessageW(&msg, 0, WM_USER, WM_USER, PM_NOREMOVE);
            _threadId = GetCurrentThreadId();
            s_current = this;

            _hook = SetWindowsHookExW(
                WH_KEYBOARD_LL,
                (nint)(delegate* unmanaged<int, nint, nint, nint>)&HookProc,
                GetModuleHandleW(null),
                0);
            if (_hook == 0)
            {
                error = new Win32Exception(Marshal.GetLastPInvokeError());
                ready.Set();
                return;
            }
            ready.Set();

            // 低级钩子的回调在 GetMessage 内部执行，不需要分发消息
            while (GetMessageW(&msg, 0, 0, 0) > 0)
            {
                if (msg.message == WM_APP_LOCK) LockWorkStation();
            }

            UnhookWindowsHookEx(_hook);
            _hook = 0;
        })
        {
            IsBackground = true,
            Name = "KeyboardHook",
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
        ready.Wait();
        if (error != null) throw error;
    }

    [UnmanagedCallersOnly]
    static nint HookProc(int nCode, nint wParam, nint lParam)
    {
        var self = s_current;
        if (nCode >= 0 && self != null)
        {
            try
            {
                if (self.OnKey(in *(KBDLLHOOKSTRUCT*)lParam)) return 1;
            }
            catch
            {
                // 异常不能穿出非托管回调，否则进程直接崩溃
            }
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    /// <returns>true = 吞掉这个事件</returns>
    bool OnKey(in KBDLLHOOKSTRUCT k)
    {
        if (k.dwExtraInfo == InjectedTag) return false;
        uint vk = k.vkCode;
        if (vk is 0 or 0xE7 or 0xFF) return false; // VK_PACKET 等非真实按键

        bool up = (k.flags & LLKHF_UP) != 0;
        var id = new KeyId((ushort)vk, (ushort)k.scanCode, (k.flags & LLKHF_EXTENDED) != 0);
        bool swallow = false, changed = false, lockNow = false;

        lock (_gate)
        {
            if (_swallowUntilUp.Count > 0 && _swallowUntilUp.Contains(id))
            {
                if (up) _swallowUntilUp.Remove(id);
                return true;
            }
            if (!_enabled) return false;

            if (vk == VK_SCROLL)
            {
                // Scroll Lock 只用来触发：不拦截（按偶数下指示灯会恢复原状），也永远不会被按住
                if (up) { _scrollDown = false; return false; }
                if (_scrollDown) return false; // 按住不放产生的自动重复
                _scrollDown = true;

                _scrollCount = _scrollCount > 0 && k.time - _lastScrollTime <= ScrollGapMs ? _scrollCount + 1 : 1;
                _lastScrollTime = k.time;
                if (_scrollCount >= ReleaseAllPresses)
                {
                    _scrollCount = 0;
                    ReleaseAllLocked($"连按 {ReleaseAllPresses} 下 Scroll Lock，全部松开");
                }
                changed = true;
            }
            else if (!up)
            {
                bool armed = IsArmedLocked(k.time);
                bool wasCounting = _scrollCount >= 2;
                _scrollCount = 0; // Scroll Lock 连按中间夹了别的键，重新计数
                if (armed)
                {
                    _swallowUntilUp.Add(id);
                    ToggleLocked(id);
                    swallow = true;
                    changed = true;
                }
                else if (wasCounting)
                {
                    changed = true; // 等待已超时或连按到一半被打断，刷新界面
                }

                if (!armed && vk == 'L' && _held.Count > 0 && IsWinDown())
                {
                    // Win+L：先把按住的键全部松开再锁屏，免得锁屏界面上 Shift 之类还按着
                    ReleaseAllLocked("锁屏前全部松开");
                    _swallowUntilUp.Add(id);
                    swallow = true;
                    lockNow = true;
                    changed = true;
                }
            }
            else if (_held.Contains(id))
            {
                // 被按住的键又被亲手按了一下：拦掉松手，保持按住
                swallow = true;
            }
        }

        if (lockNow) PostThreadMessageW(_threadId, WM_APP_LOCK, 0, 0);
        if (changed) Changed?.Invoke();
        return swallow;
    }

    static bool IsWinDown() =>
        (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

    /// <summary>刚好连按了 2 下 Scroll Lock，且第 2 下之后还没超过 1 秒。</summary>
    bool IsArmedLocked(uint now) => _scrollCount == 2 && now - _lastScrollTime <= ArmWindowMs;

    void ToggleLocked(KeyId id)
    {
        if (id.Vk == VK_SCROLL) return; // Scroll Lock 不可被按住
        int i = _held.IndexOf(id);
        if (i >= 0)
        {
            _held.RemoveAt(i);
            Send(id, up: true);
            SetLastAction($"松开 {id.Name}");
        }
        else
        {
            _held.Add(id);
            Send(id, up: false);
            SetLastAction($"按住 {id.Name}");
        }
    }

    void ReleaseAllLocked(string reason)
    {
        for (int i = _held.Count - 1; i >= 0; i--) Send(_held[i], up: true);
        _held.Clear();
        SetLastAction(reason);
    }

    void SetLastAction(string text) => _lastAction = $"{text}（{DateTime.Now:HH:mm:ss}）";

    static bool Send(KeyId id, bool up)
    {
        // 同时带虚拟键码和扫描码：认虚拟键码的程序和认扫描码的游戏都能收到
        INPUT input = default;
        input.type = INPUT_KEYBOARD;
        input.u.ki.wVk = id.Vk;
        input.u.ki.wScan = id.Scan != 0 ? id.Scan : (ushort)(MapVirtualKeyW(id.Vk, MAPVK_VK_TO_VSC) & 0xFF);
        input.u.ki.dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (id.Extended ? KEYEVENTF_EXTENDEDKEY : 0);
        input.u.ki.dwExtraInfo = InjectedTag;
        return SendInput(1, &input, sizeof(INPUT)) == 1;
    }

    // ---- 供界面调用 ----

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            _scrollCount = 0;
            if (enabled) SetLastAction("总开关开启");
            else ReleaseAllLocked("总开关关闭，全部松开");
        }
        Changed?.Invoke();
    }

    public void ReleaseAll()
    {
        lock (_gate)
        {
            if (_held.Count == 0) return;
            ReleaseAllLocked("全部松开");
        }
        Changed?.Invoke();
    }

    public void Release(KeyId id)
    {
        lock (_gate)
        {
            if (!_held.Remove(id)) return;
            Send(id, up: true);
            SetLastAction($"松开 {id.Name}");
        }
        Changed?.Invoke();
    }

    /// <summary>锁屏、断开远程桌面、睡眠：全部松开，并记下来解锁后再补发一次抬起。</summary>
    public void OnSessionInactive()
    {
        lock (_gate)
        {
            _scrollCount = 0;
            if (_held.Count == 0) return;
            _releasedOnLock.AddRange(_held);
            ReleaseAllLocked("会话锁定，全部松开");
        }
        Changed?.Invoke();
    }

    /// <summary>锁屏期间注入可能被系统丢弃，解锁后补发抬起。</summary>
    public void OnSessionActive()
    {
        lock (_gate)
        {
            foreach (var id in _releasedOnLock)
            {
                if (!_held.Contains(id)) Send(id, up: true);
            }
            _releasedOnLock.Clear();
        }
    }

    public EngineSnapshot Snapshot()
    {
        lock (_gate)
        {
            uint elapsed = (uint)Environment.TickCount - _lastScrollTime;
            int remaining = _scrollCount == 2 && elapsed < ArmWindowMs ? (int)(ArmWindowMs - elapsed) : 0;
            bool releaseAllPending = _scrollCount == ReleaseAllPresses - 1 && elapsed < ScrollGapMs;
            return new EngineSnapshot(_enabled, remaining > 0, remaining, releaseAllPending, _held.ToArray(), _lastAction);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_gate)
        {
            if (_held.Count > 0) ReleaseAllLocked("退出");
        }
        if (_thread != null)
        {
            PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
            _thread.Join(2000);
        }
        s_current = null;
    }
}
