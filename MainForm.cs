using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Microsoft.Win32;

namespace KeyHolder;

internal sealed class MainForm : Form
{
    static readonly Color OnColor = Color.FromArgb(46, 125, 50);
    static readonly Color OffColor = Color.FromArgb(117, 117, 117);
    static readonly Color ArmedColor = Color.FromArgb(21, 101, 192);
    static readonly Color HoldColor = Color.FromArgb(216, 67, 21);

    readonly KeyHoldEngine _engine;
    readonly Button _master = new();
    readonly Label _status = new();
    readonly Label _last = new();
    readonly ListBox _list = new();
    readonly Button _releaseSel = new();
    readonly Button _releaseAll = new();
    readonly Button _exit = new();
    readonly NotifyIcon _tray = new();
    readonly ToolStripMenuItem _trayMaster = new("总开关");
    readonly System.Windows.Forms.Timer _armTimer = new() { Interval = 100 };

    Icon? _formIcon, _trayIcon;
    string _iconKey = "";
    KeyId[] _shownHeld = [];
    bool _exiting, _balloonShown;

    public MainForm(KeyHoldEngine engine)
    {
        _engine = engine;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "KeyHolder 按键保持";
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        ClientSize = new Size(400, 470);
        MinimumSize = new Size(360, 400);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(14, 12, 14, 12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); // 总开关
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 状态
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 上次操作
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 列表
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 按钮
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 说明

        _master.Dock = DockStyle.Fill;
        _master.FlatStyle = FlatStyle.Flat;
        _master.FlatAppearance.BorderSize = 0;
        _master.ForeColor = Color.White;
        _master.Font = new Font(Font.FontFamily, 12.5F, FontStyle.Bold);
        _master.Margin = new Padding(0, 0, 0, 6);
        _master.Cursor = Cursors.Hand;
        _master.Click += (_, _) =>
        {
            _engine.SetEnabled(!_engine.Snapshot().Enabled);
            ActiveControl = _list;
        };

        _status.AutoSize = true;
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _status.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        _status.Margin = new Padding(0, 6, 0, 2);

        _last.AutoSize = true;
        _last.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _last.ForeColor = SystemColors.GrayText;
        _last.Margin = new Padding(0, 0, 0, 6);

        var group = new GroupBox
        {
            Text = "正在按住（双击某一项可单独松开）",
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 6, 8, 8),
        };
        _list.Dock = DockStyle.Fill;
        _list.IntegralHeight = false;
        _list.Font = new Font(Font.FontFamily, 11.5F);
        _list.BorderStyle = BorderStyle.None;
        _list.DoubleClick += (_, _) => ReleaseSelected();
        _list.SelectedIndexChanged += (_, _) => _releaseSel.Enabled = _list.SelectedIndex >= 0;
        group.Controls.Add(_list);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 4),
        };
        for (int i = 0; i < 3; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        SetupButton(_releaseSel, "松开选中", ReleaseSelected);
        SetupButton(_releaseAll, "全部松开", _engine.ReleaseAll);
        SetupButton(_exit, "退出程序", ExitApp);
        buttons.Controls.Add(_releaseSel, 0, 0);
        buttons.Controls.Add(_releaseAll, 1, 0);
        buttons.Controls.Add(_exit, 2, 0);

        var hint = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 6, 0, 0),
            Text = "用法：1 秒内按两下 Scroll Lock，再在 1 秒内按要按住的键（Shift、Ctrl 等也行）。" +
                   "可依次按住多个键；对已按住的键再做一遍即松开。连按 4 下 Scroll Lock 松开全部。\n" +
                   "关闭窗口会缩到托盘，后台照常工作。",
        };

        root.Controls.Add(_master, 0, 0);
        root.Controls.Add(_status, 0, 1);
        root.Controls.Add(_last, 0, 2);
        root.Controls.Add(group, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        root.Controls.Add(hint, 0, 5);
        Controls.Add(root);

        // 托盘
        var menu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem("显示主窗口", null, (_, _) => ShowMain()) { Font = new Font(menu.Font, FontStyle.Bold) };
        _trayMaster.Click += (_, _) => _engine.SetEnabled(!_engine.Snapshot().Enabled);
        menu.Items.Add(showItem);
        menu.Items.Add(_trayMaster);
        menu.Items.Add(new ToolStripMenuItem("全部松开", null, (_, _) => _engine.ReleaseAll()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitApp()));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();
        _tray.Visible = true;

        ResumeLayout(true);

        _armTimer.Tick += (_, _) => RefreshUi();
        _engine.Changed += OnEngineChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        RefreshUi();
        ActiveControl = _list;
    }

    static void SetupButton(Button b, string text, Action onClick)
    {
        b.Text = text;
        b.Dock = DockStyle.Fill;
        b.Height = 34;
        b.Margin = new Padding(3, 0, 3, 0);
        b.Click += (_, _) => onClick();
    }

    void OnEngineChanged()
    {
        try
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(RefreshUi);
        }
        catch (InvalidOperationException) { }
    }

    void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
            case SessionSwitchReason.SessionLogoff:
                _engine.OnSessionInactive();
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                _engine.OnSessionActive();
                break;
        }
    }

    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) _engine.OnSessionInactive();
        else if (e.Mode == PowerModes.Resume) _engine.OnSessionActive();
    }

    void RefreshUi()
    {
        if (IsDisposed) return;
        var s = _engine.Snapshot();

        _master.Text = s.Enabled ? "●  总开关：已开启" : "○  总开关：已关闭";
        _master.BackColor = s.Enabled ? OnColor : OffColor;
        _master.FlatAppearance.MouseOverBackColor = ControlPaint.Light(_master.BackColor, 0.15F);
        _master.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(_master.BackColor, 0.05F);

        if (!s.Enabled)
        {
            _status.Text = "已关闭：不响应触发";
            _status.ForeColor = OffColor;
        }
        else if (s.Armed)
        {
            _status.Text = $"等待按键…（还剩 {s.ArmRemainingMs / 1000.0:0.0} 秒）";
            _status.ForeColor = ArmedColor;
        }
        else if (s.ReleaseAllPending)
        {
            _status.Text = "再按一下 Scroll Lock 全部松开";
            _status.ForeColor = ArmedColor;
        }
        else if (s.Held.Count > 0)
        {
            _status.Text = $"正在按住 {s.Held.Count} 个键";
            _status.ForeColor = HoldColor;
        }
        else
        {
            _status.Text = "空闲";
            _status.ForeColor = SystemColors.ControlText;
        }
        _last.Text = s.LastAction is null ? "上次操作：无" : $"上次操作：{s.LastAction}";

        if (!s.Held.SequenceEqual(_shownHeld))
        {
            var selected = _list.SelectedIndex >= 0 && _list.SelectedIndex < _shownHeld.Length
                ? _shownHeld[_list.SelectedIndex]
                : (KeyId?)null;
            _shownHeld = s.Held.ToArray();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var id in _shownHeld) _list.Items.Add(id.Name);
            if (selected is KeyId sel)
            {
                int idx = Array.IndexOf(_shownHeld, sel);
                if (idx >= 0) _list.SelectedIndex = idx;
            }
            _list.EndUpdate();
        }
        _releaseSel.Enabled = _list.SelectedIndex >= 0;
        _releaseAll.Enabled = s.Held.Count > 0;
        _trayMaster.Checked = s.Enabled;

        UpdateIcons(s);
        string trayText = !s.Enabled ? "已关闭"
            : s.Held.Count == 0 ? "空闲"
            : "按住：" + string.Join("、", s.Held.Select(k => k.Name));
        trayText = "KeyHolder - " + trayText;
        _tray.Text = trayText.Length > 127 ? trayText[..124] + "…" : trayText;

        _armTimer.Enabled = s.Armed || s.ReleaseAllPending;
    }

    void UpdateIcons(EngineSnapshot s)
    {
        Color color = !s.Enabled ? OffColor
            : s.Armed || s.ReleaseAllPending ? ArmedColor
            : s.Held.Count > 0 ? HoldColor
            : OnColor;
        string text = s.Enabled && s.Held.Count > 0 ? Math.Min(s.Held.Count, 99).ToString() : "K";
        string key = $"{color.ToArgb()}|{text}";
        if (key == _iconKey) return;
        _iconKey = key;

        var oldForm = _formIcon;
        var oldTray = _trayIcon;
        _formIcon = MakeIcon(color, text, 32);
        _trayIcon = MakeIcon(color, text, Math.Max(16, SystemInformation.SmallIconSize.Width));
        Icon = _formIcon;
        _tray.Icon = _trayIcon;
        oldForm?.Dispose();
        oldTray?.Dispose();
    }

    static Icon MakeIcon(Color fill, string text, int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float r = size * 0.24F;
            var rect = new RectangleF(0, 0, size - 1, size - 1);
            using var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
            path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
            path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);

            float em = size * (text.Length > 1 ? 0.52F : 0.68F);
            using var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, size * 0.03F, size, size), sf);
        }

        nint h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally
        {
            Native.DestroyIcon(h);
        }
    }

    void ReleaseSelected()
    {
        int i = _list.SelectedIndex;
        if (i >= 0 && i < _shownHeld.Length) _engine.Release(_shownHeld[i]);
    }

    public void ShowMain()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    void ExitApp()
    {
        _exiting = true;
        _engine.ReleaseAll();
        _tray.Visible = false;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            // 点 × 只是缩到托盘
            e.Cancel = true;
            Hide();
            if (!_balloonShown)
            {
                _balloonShown = true;
                _tray.ShowBalloonTip(3000, "KeyHolder 仍在后台运行", "双击托盘图标打开窗口，右键菜单可以退出。", ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine.Changed -= OnEngineChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _armTimer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _formIcon?.Dispose();
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
