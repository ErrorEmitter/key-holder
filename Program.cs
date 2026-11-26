namespace KeyHolder;

internal static class Program
{
    const string MutexName = @"Local\KeyHolder.Instance.7C3E1A52";
    const string ShowEventName = @"Local\KeyHolder.Show.7C3E1A52";

    [STAThread]
    static void Main()
    {
        // 只允许一个实例：第二次启动时把已运行的窗口叫出来
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool first);
        if (!first)
        {
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                ev.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            return;
        }

        ApplicationConfiguration.Initialize();

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var engine = new KeyHoldEngine();
        try
        {
            engine.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法安装键盘钩子：{ex.Message}", "KeyHolder", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 程序意外崩溃时也尽量松开所有键
        AppDomain.CurrentDomain.UnhandledException += (_, _) => engine.ReleaseAll();
        Application.ThreadException += (_, e) =>
        {
            engine.ReleaseAll();
            MessageBox.Show(e.Exception.ToString(), "KeyHolder 出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        using var form = new MainForm(engine);
        var wait = ThreadPool.RegisterWaitForSingleObject(
            showEvent,
            (_, _) =>
            {
                try { form.BeginInvoke(form.ShowMain); }
                catch (InvalidOperationException) { }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        Application.Run(form);
        wait.Unregister(null);
    }
}
