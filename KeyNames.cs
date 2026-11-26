namespace KeyHolder;

/// <summary>把按键转换成界面上显示的名字。</summary>
internal static unsafe class KeyNames
{
    static readonly Dictionary<ushort, string> Fixed = new()
    {
        [0xA0] = "左 Shift",
        [0xA1] = "右 Shift",
        [0xA2] = "左 Ctrl",
        [0xA3] = "右 Ctrl",
        [0xA4] = "左 Alt",
        [0xA5] = "右 Alt",
        [0x5B] = "左 Win",
        [0x5C] = "右 Win",
        [0x5D] = "菜单键",
        [0x20] = "空格",
        [0x09] = "Tab",
        [0x08] = "Backspace",
        [0x1B] = "Esc",
        [0x14] = "Caps Lock",
        [0x90] = "Num Lock",
        [0x2C] = "Print Screen",
        [0x13] = "Pause",
    };

    // 方向键和编辑键：非扩展扫描码表示来自小键盘（Num Lock 关闭时）
    static readonly Dictionary<ushort, string> NavKeys = new()
    {
        [0x25] = "←",
        [0x26] = "↑",
        [0x27] = "→",
        [0x28] = "↓",
        [0x24] = "Home",
        [0x23] = "End",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
    };

    public static string Get(KeyId id)
    {
        ushort vk = id.Vk;
        if (Fixed.TryGetValue(vk, out var name)) return name;
        if (vk == 0x0D) return id.Extended ? "小键盘 Enter" : "Enter";
        if (NavKeys.TryGetValue(vk, out name)) return id.Extended ? name : "小键盘 " + name;
        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        if (vk is >= 0x60 and <= 0x69) return "小键盘 " + (vk - 0x60);

        if (id.Scan != 0)
        {
            char* buf = stackalloc char[64];
            int lParam = (id.Scan << 16) | (id.Extended ? 1 << 24 : 0);
            int len = Native.GetKeyNameTextW(lParam, buf, 64);
            if (len > 0) return new string(buf, 0, len);
        }
        return $"VK 0x{vk:X2}";
    }
}
