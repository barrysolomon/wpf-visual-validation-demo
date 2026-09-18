using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace WpfVisualValidationDemo.VisualTests;

/// Launches the demo app, exposes its main window for UI Automation, and captures it as a Bitmap.
internal sealed class AppSession : IDisposable
{
    private readonly Process _process;

    public IntPtr Handle { get; }
    public AutomationElement Window { get; }

    private AppSession(Process process, IntPtr handle)
    {
        _process = process;
        Handle = handle;
        Window = AutomationElement.FromHandle(handle);
    }

    public static AppSession Launch()
    {
        SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);

        var exe = Path.Combine(AppContext.BaseDirectory, "WpfVisualValidationDemo.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("Build the app first.", exe);

        var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Failed to start the app.");

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited) throw new InvalidOperationException($"App exited early with code {process.ExitCode}.");
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                process.WaitForInputIdle(5000);
                ParkCursor(process.MainWindowHandle);
                Thread.Sleep(750); // let fonts/layout settle
                return new AppSession(process, process.MainWindowHandle);
            }
            Thread.Sleep(100);
        }
        process.Kill();
        throw new TimeoutException("App window did not appear.");
    }

    /// Invokes a button by its accessible name (the button's content).
    public void Click(string name) => Invoke(Find(AutomationElement.NameProperty, name));

    /// Invokes a control by its x:Name (WPF exposes it as the UIA AutomationId).
    public void ClickById(string automationId) => Invoke(Find(AutomationElement.AutomationIdProperty, automationId));

    /// Opens a ComboBox (by x:Name) and selects the item with the given text.
    public void SelectComboItem(string automationId, string itemName)
    {
        var combo = Find(AutomationElement.AutomationIdProperty, automationId);
        var expand = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expand.Expand(); // items only enter the UIA tree once the popup has opened
        var item = WaitFor(() => combo.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, itemName)))
            ?? throw new InvalidOperationException($"No item '{itemName}' in '{automationId}'.");
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        expand.Collapse();
        Settle();
    }

    /// Replaces the text of a TextBox (by x:Name).
    public void SetText(string automationId, string text)
    {
        var element = Find(AutomationElement.AutomationIdProperty, automationId);
        ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);
    }

    /// Flips a CheckBox or ToggleButton (by x:Name).
    public void Toggle(string automationId)
    {
        var element = Find(AutomationElement.AutomationIdProperty, automationId);
        ((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
    }

    /// Scrolls the main ScrollViewer to the given vertical position (0–100).
    public void ScrollTo(double percent)
    {
        var scroller = Window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true))
            ?? throw new InvalidOperationException("No scrollable element found.");
        ((ScrollPattern)scroller.GetCurrentPattern(ScrollPattern.Pattern)).SetScrollPercent(ScrollPattern.NoScroll, percent);
        Settle();
    }

    /// Polls `probe` until it returns an element or the timeout elapses.
    private static AutomationElement? WaitFor(Func<AutomationElement?> probe, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var element = probe();
            if (element is not null || DateTime.UtcNow >= deadline) return element;
            Thread.Sleep(50);
        }
    }

    private AutomationElement Find(AutomationProperty property, string value) =>
        Window.FindFirst(TreeScope.Descendants, new PropertyCondition(property, value))
            ?? throw new InvalidOperationException($"No element with {property.ProgrammaticName} = '{value}'.");

    private static void Invoke(AutomationElement element) =>
        ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    /// Waits for the app's message loop to go idle, then for the render thread to present a frame.
    private void Settle()
    {
        _process.WaitForInputIdle(2000);
        Thread.Sleep(250);
    }

    /// Blocks until a text element containing `text` appears (UIA Invoke returns before WPF processes the click).
    public void WaitForText(string text, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var textElements = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
        while (DateTime.UtcNow < deadline)
        {
            foreach (AutomationElement e in Window.FindAll(TreeScope.Descendants, textElements))
                if (e.Current.Name.Contains(text, StringComparison.Ordinal))
                {
                    Settle();
                    return;
                }
            Thread.Sleep(50);
        }
        throw new TimeoutException($"Text '{text}' did not appear within {timeoutMs} ms.");
    }

    /// Renders the window's client area (no title bar) into a bitmap at device pixels.
    public Bitmap Capture()
    {
        if (!GetClientRect(Handle, out var rect)) throw new InvalidOperationException("GetClientRect failed.");
        var bmp = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var hdc = g.GetHdc();
        try
        {
            if (!PrintWindow(Handle, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT))
                throw new InvalidOperationException("PrintWindow failed.");
        }
        finally { g.ReleaseHdc(hdc); }
        return bmp;
    }

    public void Dispose()
    {
        if (!_process.HasExited) _process.Kill();
        _process.Dispose();
    }

    /// Moves the mouse just outside the window so no control is captured in its hover state.
    private static void ParkCursor(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return;
        SetCursorPos(Math.Max(0, r.Left - 20), Math.Max(0, r.Top - 20));
    }

    private const uint PW_CLIENTONLY = 0x1;
    private const uint PW_RENDERFULLCONTENT = 0x2;
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
