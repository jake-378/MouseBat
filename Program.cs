using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Diagnostics;
using Microsoft.Win32;
using HidSharp;

namespace MouseBat;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "MouseBat.SingleInstance", out bool first);
        if (!first) return;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new BatteryContext());
    }
}

internal sealed class BatteryContext : ApplicationContext
{
    private const int VendorId = 0x0B05;
    private const int ProductId = 0x197F;
    private const int LowThreshold = 20;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer;
    private readonly ToolStripMenuItem stateItem;
    private readonly Control dispatcher = new();
    private readonly BatteryWidget widget;
    private readonly System.Windows.Forms.Timer dockTimer;
    private int? lastLevel;
    private bool charging;
    private bool querying;
    private bool exiting;

    public BatteryContext()
    {
        _ = dispatcher.Handle;
        var menu = new ContextMenuStrip();
        stateItem = new ToolStripMenuItem("Looking for ROG Gladius III Wireless…") { Enabled = false };
        menu.Items.Add(stateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh now", null, (_, _) => RefreshBattery());
        var placement = new ToolStripMenuItem("Widget position");
        AddPlacement(placement, "Before notification area", WidgetPlacement.BeforeTray);
        AddPlacement(placement, "Left side of taskbar", WidgetPlacement.Left);
        AddPlacement(placement, "Center of taskbar", WidgetPlacement.Center);
        AddPlacement(placement, "Right side of taskbar", WidgetPlacement.Right);
        menu.Items.Add(placement);
        menu.Items.Add(new ToolStripMenuItem("Drag the widget to fine-tune its position") { Enabled = false });
        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = IsStartupEnabled() };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add("About MouseBat", null, (_, _) => MessageBox.Show(
            "MouseBat shows the battery level of an ASUS ROG Gladius III Wireless.\n\nLow battery is marked in red at 20% and below.",
            "MouseBat", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        tray = new NotifyIcon
        {
            Visible = true,
            Text = "MouseBat — looking for your mouse",
            Icon = DrawIcon(null, false),
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => RefreshBattery();
        widget = new BatteryWidget(RefreshBattery) { ContextMenuStrip = menu };
        widget.Show();
        dockTimer = new System.Windows.Forms.Timer { Interval = 500 };
        dockTimer.Tick += (_, _) => widget.UpdateDockPosition();
        dockTimer.Start();
        timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        timer.Tick += (_, _) => RefreshBattery();
        timer.Start();
        RefreshBattery();
    }

    private void AddPlacement(ToolStripMenuItem parent, string label, WidgetPlacement placement)
    {
        parent.DropDownItems.Add(label, null, (_, _) => widget.SetPlacement(placement));
    }

    private void RefreshBattery()
    {
        if (querying) return;
        querying = true;
        stateItem.Text = "Checking mouse battery…";
        _ = Task.Run(ReadBattery).ContinueWith(task =>
        {
            if (exiting) return;
            dispatcher.BeginInvoke(() =>
            {
                querying = false;
                if (task.Status == TaskStatus.RanToCompletion && task.Result is { } result)
                {
                    lastLevel = result.Level;
                    charging = result.Charging;
                    int level = result.Level;
                    stateItem.Text = $"ROG Gladius III Wireless: {level}%{(charging ? " (charging)" : "")}";
                    tray.Text = $"MouseBat — ROG Gladius III Wireless: {level}%{(charging ? " · charging" : "")}";
                    SetTrayIcon(DrawIcon(level, charging));
                    widget.SetBattery(level, charging, isSleeping: false);
                }
                else
                {
                    stateItem.Text = lastLevel is { } value
                        ? $"ROG Gladius III Wireless: {value}% (not responding)"
                        : "Mouse not responding — wake it or check its USB receiver";
                    tray.Text = "MouseBat — mouse asleep or unavailable";
                    SetTrayIcon(DrawIcon(lastLevel, charging, sleeping: true));
                    widget.SetBattery(lastLevel, charging, isSleeping: true);
                }
            });
        }, TaskScheduler.Default);
    }

    private static BatteryReading? ReadBattery()
    {
        foreach (var device in DeviceList.Local.GetHidDevices(VendorId, ProductId))
        {
            // ASUS exposes the same read-only 0x12/0x07 battery request on its
            // vendor HID collection when connected by cable or its 2.4 GHz receiver.
            if (device.GetMaxOutputReportLength() < 65 || device.GetMaxInputReportLength() < 65) continue;
            try
            {
                using HidStream stream = device.Open();
                stream.ReadTimeout = 700;
                stream.WriteTimeout = 700;
                var request = new byte[65]; // report ID 0 followed by the 64-byte command frame
                request[1] = 0x12;
                request[2] = 0x07;
                stream.Write(request);
                var reply = new byte[65];
                var elapsed = Stopwatch.StartNew();
                while (elapsed.ElapsedMilliseconds < 900)
                {
                    int count;
                    try { count = stream.Read(reply, 0, reply.Length); }
                    catch (TimeoutException) { break; }
                    if (count < 11) continue;
                    int offset = reply[0] == 0 && reply[1] == 0x12 ? 1 : 0;
                    if (reply[offset] != 0x12 || reply[offset + 1] != 0x07) continue;
                    int level = reply[offset + 4];
                    bool isCharging = reply[offset + 9] != 0;
                    if (level is >= 1 and <= 100) return new BatteryReading(level, isCharging);
                    // An idle mouse reports 0; it is not an empty battery.
                    if (level == 0) return null;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    private static Icon DrawIcon(int? level, bool isCharging, bool sleeping = false)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);
        Color fg = sleeping ? Color.Gray : Color.White;
        Color percentColor = !sleeping && level is <= LowThreshold ? Color.FromArgb(255, 65, 65) : fg;
        using var body = new SolidBrush(Color.FromArgb(28, 31, 40));
        using var stroke = new Pen(fg, 1.5f);
        // A simple, original top-down mouse glyph beside a readable percentage.
        g.FillRoundedRectangle(body, 2, 5, 11, 21, 5);
        g.DrawRoundedRectangle(stroke, 2, 5, 11, 21, 5);
        g.DrawLine(stroke, 7.5f, 5.5f, 7.5f, 12.3f);
        g.DrawLine(stroke, 5.4f, 10.7f, 9.6f, 10.7f);
        string label = level is { } n ? n.ToString() : "--";
        using var font = new Font("Segoe UI", label.Length == 3 ? 9.0f : 11.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(percentColor);
        g.DrawString(label, font, brush, new PointF(label.Length == 3 ? 14.5f : 15.5f, 7.3f));
        using var small = new Font("Segoe UI", 6.2f, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("%", small, brush, new PointF(24.5f, 19.2f));
        if (isCharging && !sleeping)
        {
            using var charge = new SolidBrush(Color.FromArgb(88, 204, 255));
            g.FillEllipse(charge, 25, 2, 5, 5);
        }
        IntPtr hIcon = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
        finally { DestroyIcon(hIcon); }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        return key?.GetValue("MouseBat") is not null;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        if (enabled) key.SetValue("MouseBat", $"\"{Application.ExecutablePath}\"");
        else key.DeleteValue("MouseBat", false);
    }

    private void SetTrayIcon(Icon icon)
    {
        Icon? old = tray.Icon;
        tray.Icon = icon;
        old?.Dispose();
    }

    protected override void ExitThreadCore()
    {
        exiting = true;
        timer.Stop();
        dockTimer.Stop();
        widget.Close();
        widget.Dispose();
        tray.Visible = false;
        tray.Dispose();
        timer.Dispose();
        dockTimer.Dispose();
        dispatcher.Dispose();
        base.ExitThreadCore();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
    private sealed class BatteryReading
    {
        public BatteryReading(int level, bool charging) { Level = level; Charging = charging; }
        public int Level { get; }
        public bool Charging { get; }
    }
}

internal enum WidgetPlacement
{
    BeforeTray,
    Left,
    Center,
    Right,
    Custom
}

internal sealed class BatteryWidget : Form
{
    private const int LowThreshold = 20;
    private readonly Action refreshBattery;
    private readonly ToolTip toolTip = new();
    private WidgetPlacement placement;
    private int customOffset;
    private int? level;
    private bool charging;
    private bool sleeping = true;
    private bool dragging;
    private bool dragged;
    private Point dragScreenStart;
    private Point dragWindowStart;

    public BatteryWidget(Action refreshBattery)
    {
        this.refreshBattery = refreshBattery;
        Text = "MouseBat battery";
        AccessibleName = "MouseBat battery widget";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(84, 34);
        MinimumSize = Size;
        MaximumSize = Size;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        placement = ReadPlacement();
        customOffset = ReadCustomOffset();
        toolTip.SetToolTip(this, "MouseBat — ROG Gladius III Wireless battery. Left-click to refresh; right-click for options; drag to move.");
        MouseDown += WidgetMouseDown;
        MouseMove += WidgetMouseMove;
        MouseUp += WidgetMouseUp;
        Shown += (_, _) => UpdateDockPosition();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00000080 | 0x08000000; // tool window; never activate the taskbar widget
            return cp;
        }
    }

    public void SetBattery(int? batteryLevel, bool isCharging, bool isSleeping)
    {
        level = batteryLevel;
        charging = isCharging;
        sleeping = isSleeping;
        string value = level is { } n ? $"{n}%" : "--%";
        string status = sleeping ? "mouse asleep or unavailable" : charging ? "charging" : "on battery";
        toolTip.SetToolTip(this, $"ROG Gladius III Wireless: {value} ({status}). Left-click to refresh; right-click for options; drag to move.");
        AccessibleDescription = $"ROG Gladius III Wireless battery {value}, {status}.";
        Invalidate();
    }

    public void SetPlacement(WidgetPlacement newPlacement)
    {
        placement = newPlacement;
        SavePlacement();
        UpdateDockPosition();
    }

    public void UpdateDockPosition()
    {
        if (dragging || IsDisposed || !IsHandleCreated) return;
        Rectangle taskbar = GetTaskbarBounds();
        int y = taskbar.Top + Math.Max(0, (taskbar.Height - Height) / 2);
        int x;
        Rectangle tray = GetTrayBounds();
        switch (placement)
        {
            case WidgetPlacement.Left:
                x = taskbar.Left + Math.Max(56, taskbar.Height + 8);
                break;
            case WidgetPlacement.Center:
                x = taskbar.Left + (taskbar.Width - Width) / 2;
                break;
            case WidgetPlacement.Right:
                x = (tray.IsEmpty ? taskbar.Right : tray.Left) - Width * 2 - 10;
                break;
            case WidgetPlacement.Custom:
                x = taskbar.Left + customOffset;
                break;
            default:
                x = (tray.IsEmpty ? taskbar.Right : tray.Left) - Width - 8;
                break;
        }
        x = Math.Max(taskbar.Left + 2, Math.Min(x, taskbar.Right - Width - 2));
        Location = new Point(x, y);
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
    }

    private void WidgetMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        dragging = true;
        dragged = false;
        dragScreenStart = Cursor.Position;
        dragWindowStart = Location;
        Capture = true;
    }

    private void WidgetMouseMove(object? sender, MouseEventArgs e)
    {
        if (!dragging) return;
        Point cursor = Cursor.Position;
        int dx = cursor.X - dragScreenStart.X;
        int dy = cursor.Y - dragScreenStart.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > 2) dragged = true;
        Rectangle taskbar = GetTaskbarBounds();
        int x = Math.Max(taskbar.Left + 2, Math.Min(dragWindowStart.X + dx, taskbar.Right - Width - 2));
        int y = taskbar.Top + Math.Max(0, (taskbar.Height - Height) / 2);
        Location = new Point(x, y);
    }

    private void WidgetMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !dragging) return;
        dragging = false;
        Capture = false;
        if (dragged)
        {
            placement = WidgetPlacement.Custom;
            customOffset = Location.X - GetTaskbarBounds().Left;
            SavePlacement();
        }
        else
        {
            toolTip.Show("Refreshing mouse battery…", this, Width / 2, Height, 1500);
            refreshBattery();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        Color textColor = sleeping ? Color.FromArgb(170, 174, 185) : level is <= LowThreshold
            ? Color.FromArgb(255, 78, 78)
            : Color.White;
        using var mouseLine = new Pen(Color.White, 1.25f);
        g.DrawRoundedRectangle(mouseLine, 4, 6, 16, 22, 7);
        g.DrawLine(mouseLine, 12, 6.5f, 12, 13.5f);
        g.DrawLine(mouseLine, 10.5f, 11, 13.5f, 11);

        string label = level is { } value ? $"{value}%" : "--%";
        using var font = new Font("Segoe UI", label.Length >= 4 ? 15.5f : 16.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var labelBrush = new SolidBrush(textColor);
        using var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(label, font, labelBrush, new RectangleF(27, 0, Width - 29, Height), format);
    }

    private void SavePlacement()
    {
        using var key = Registry.CurrentUser.CreateSubKey("Software\\MouseBat");
        key.SetValue("WidgetPlacement", placement.ToString());
        key.SetValue("WidgetOffset", customOffset, RegistryValueKind.DWord);
    }

    private static WidgetPlacement ReadPlacement()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Software\\MouseBat");
        string? value = key?.GetValue("WidgetPlacement") as string;
        return Enum.TryParse(value, out WidgetPlacement result) ? result : WidgetPlacement.BeforeTray;
    }

    private static int ReadCustomOffset()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Software\\MouseBat");
        return key?.GetValue("WidgetOffset") is int value ? value : 0;
    }

    private static Rectangle GetTaskbarBounds()
    {
        IntPtr handle = FindWindow("Shell_TrayWnd", null);
        if (handle != IntPtr.Zero && GetWindowRect(handle, out NativeRect bounds))
            return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        Rectangle screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? screen;
        int height = Math.Max(36, screen.Bottom - work.Bottom);
        return new Rectangle(screen.Left, screen.Bottom - height, screen.Width, height);
    }

    private static Rectangle GetTrayBounds()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        IntPtr tray = taskbar == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray != IntPtr.Zero && GetWindowRect(tray, out NativeRect bounds))
            return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        return Rectangle.Empty;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

internal static class GraphicsExtensions
{
    public static void AddRoundedRectangle(this GraphicsPath path, RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
    }
    public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float width, float height, float radius)
    {
        using var path = RoundedPath(x, y, width, height, radius);
        g.FillPath(brush, path);
    }
    public static void DrawRoundedRectangle(this Graphics g, Pen pen, float x, float y, float width, float height, float radius)
    {
        using var path = RoundedPath(x, y, width, height, radius);
        g.DrawPath(pen, path);
    }
    private static GraphicsPath RoundedPath(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        float d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
