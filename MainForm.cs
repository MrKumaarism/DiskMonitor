using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DiskMonitor;

public class MainForm : Form
{
    private const string AppName = "DiskMonitor";
    private const string DeveloperName = "DgLogiQ";
    private const string AppVersion = "1.05";

    private const string StartupRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string StartupApprovedPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string StartupApprovedFolder =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private const string StartupValueName =
        "DiskMonitor";

    private const string SettingsRegistryPath =
        @"Software\DgLogiQ\DiskMonitor";

    private const string AlwaysOnTopValueName =
        "AlwaysOnTop";

    // Auto-collapse: seconds before expanded view collapses on its own.
    private const int AutoCollapseSeconds = 8;

    // Smooth expand/collapse animation settings
    private const int AnimDurationMs = 210;
    private System.Windows.Forms.Timer? animTimer;
    private DateTime animStartTime;
    private Size animStartSize;
    private Size animTargetSize;
    private int animCenterX;
    private int animTopY;
    private bool isAnimating;

    private readonly System.Windows.Forms.Timer refreshTimer;
    private System.Windows.Forms.Timer? collapseTimer;
    private readonly ContextMenuStrip menu;
    private ToolStripMenuItem alwaysOnTopItem = null!;
    private ToolStripMenuItem startWithWindowsItem = null!;
    private readonly List<DriveStat> drives = new();

    private bool alwaysOnTop = true;
    private bool expanded;
    private bool mouseHeld;
    private bool dragging;
    private bool infoPressed;

    private Point dragStartCursor;
    private Point dragStartWindow;

    private RectangleF infoButtonRect;

    private InfoPopup? infoPopup;

    private const int MiniHeight = 44;
    private const int ExpandedWidth = 520;

    // Drive area + footer.
    private const int ExpandedRowHeight = 80;
    private const int ExpandedFooterHeight = 40;

    private readonly Color backgroundColor =
        Color.FromArgb(9, 23, 37);

    private readonly Color borderColor =
        Color.FromArgb(42, 65, 87);

    private readonly Color mutedColor =
        Color.FromArgb(145, 172, 201);

    private readonly Color progressBackground =
        Color.FromArgb(31, 52, 72);

    private readonly Color blueColor =
        Color.FromArgb(0, 145, 255);

    private readonly Color warningColor =
        Color.FromArgb(255, 70, 70);

    public MainForm()
    {
        Text = AppName;

        // Load the icon that is embedded in this EXE
        // (set via <ApplicationIcon> in the .csproj).
        // This ensures the Start menu, Alt-Tab, and
        // Task Manager all show our custom HDD icon.
        try
        {
            Icon =
                System.Drawing.Icon
                .ExtractAssociatedIcon(
                    Application.ExecutablePath
                )!;
        }
        catch { /* fall back to default */ }

        AutoScaleMode = AutoScaleMode.None;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;

        alwaysOnTop = LoadAlwaysOnTopSetting();
        TopMost = alwaysOnTop;
        ShowInTaskbar = false;

        BackColor = backgroundColor;
        ForeColor = Color.White;

        DoubleBuffered = true;
        Opacity = 0.98;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true
        );

        menu = new ContextMenuStrip();

        menu.Items.Add(
            "Expand",
            null,
            (_, _) => ToggleExpanded()
        );

        menu.Items.Add(
            "About DiskMonitor",
            null,
            (_, _) => ToggleInfoPopup()
        );

        menu.Items.Add(
            "Refresh",
            null,
            (_, _) => UpdateDrives()
        );

        menu.Items.Add(
            new ToolStripSeparator()
        );

        alwaysOnTopItem =
            new ToolStripMenuItem(
                "Always on Top"
            );

        alwaysOnTopItem.Click +=
            (_, _) => ToggleAlwaysOnTop();

        menu.Items.Add(
            alwaysOnTopItem
        );

        startWithWindowsItem =
            new ToolStripMenuItem(
                "Start with Windows"
            );

        startWithWindowsItem!.Click +=
            (_, _) => ToggleStartWithWindows();

        menu.Items.Add(
            startWithWindowsItem
        );

        menu.Items.Add(
            new ToolStripSeparator()
        );

        menu.Items.Add(
            "Exit",
            null,
            (_, _) => Close()
        );

        ContextMenuStrip = menu;

        refreshTimer =
            new System.Windows.Forms.Timer
            {
                Interval = 5000
            };

        refreshTimer.Tick +=
            (_, _) => UpdateDrives();

        MouseDown += BeginMouseAction;
        MouseMove += ContinueMouseAction;
        MouseMove += ResetCollapseTimerOnHover;
        MouseUp += EndMouseAction;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Shown += (_, _) =>
        {
            UpdateDrives();
            ApplySize();
            PositionAtTopCenter();
            RefreshAlwaysOnTopState();
            RefreshStartWithWindowsState();

            // Guarantee startup shortcut synchronization on launch
            if (IsStartWithWindowsEnabled())
            {
                CreateStartupShortcut(Application.ExecutablePath);
                SetStartupApproved(true);
            }

            refreshTimer.Start();
        };

        FormClosed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            animTimer?.Stop();
            animTimer?.Dispose();

            collapseTimer?.Stop();
            collapseTimer?.Dispose();

            if (infoPopup != null &&
                !infoPopup.IsDisposed)
            {
                infoPopup.Close();
            }
        };

        ApplySize();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x00020000;

            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;

            return cp;
        }
    }

    private void BeginMouseAction(
        object? sender,
        MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        // i button should never start dragging
        // or expand/collapse.
        if (!expanded &&
            infoButtonRect.Contains(e.Location))
        {
            infoPressed = true;
            return;
        }

        infoPressed = false;
        mouseHeld = true;
        dragging = false;

        dragStartCursor = Cursor.Position;
        dragStartWindow = Location;

        Capture = true;
    }

    private void ContinueMouseAction(
        object? sender,
        MouseEventArgs e)
    {
        if (!mouseHeld)
            return;

        Point current = Cursor.Position;

        int dx =
            current.X - dragStartCursor.X;

        int dy =
            current.Y - dragStartCursor.Y;

        if (!dragging &&
            (Math.Abs(dx) > 4 ||
             Math.Abs(dy) > 4))
        {
            dragging = true;
        }

        if (dragging)
        {
            Location = new Point(
                dragStartWindow.X + dx,
                dragStartWindow.Y + dy
            );

            PositionInfoPopup();
        }
    }

    private void EndMouseAction(
        object? sender,
        MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (infoPressed)
        {
            infoPressed = false;

            if (!expanded &&
                infoButtonRect.Contains(e.Location))
            {
                ToggleInfoPopup();
            }

            return;
        }

        Capture = false;

        bool wasDragging = dragging;

        mouseHeld = false;
        dragging = false;

        if (!wasDragging)
            ToggleExpanded();
    }

    private bool LoadAlwaysOnTopSetting()
    {
        try
        {
            using var key =
                Registry.CurrentUser.OpenSubKey(
                    SettingsRegistryPath
                );

            if (key != null)
            {
                object? val =
                    key.GetValue(
                        AlwaysOnTopValueName
                    );

                if (val is int intVal)
                    return intVal != 0;
            }
        }
        catch { }

        // Always on top by default
        return true;
    }

    private void SetAlwaysOnTopSetting(bool enabled)
    {
        try
        {
            using var key =
                Registry.CurrentUser.CreateSubKey(
                    SettingsRegistryPath
                );

            key?.SetValue(
                AlwaysOnTopValueName,
                enabled ? 1 : 0,
                RegistryValueKind.DWord
            );
        }
        catch { }
    }

    private void RefreshAlwaysOnTopState()
    {
        if (alwaysOnTopItem != null)
            alwaysOnTopItem.Checked = alwaysOnTop;

        TopMost = alwaysOnTop;

        if (infoPopup != null &&
            !infoPopup.IsDisposed)
        {
            infoPopup.TopMost = alwaysOnTop;
        }
    }

    private void ToggleAlwaysOnTop()
    {
        alwaysOnTop = !alwaysOnTop;
        SetAlwaysOnTopSetting(alwaysOnTop);
        RefreshAlwaysOnTopState();
    }

    private static string GetStartupShortcutPath()
    {
        string startupDir =
            Environment.GetFolderPath(
                Environment.SpecialFolder.Startup
            );

        return Path.Combine(
            startupDir,
            "DiskMonitor.lnk"
        );
    }

    private static void CreateStartupShortcut(string targetExePath)
    {
        try
        {
            string shortcutPath = GetStartupShortcutPath();
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                object? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    object? shortcut = shellType.InvokeMember(
                        "CreateShortcut",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null,
                        shell,
                        new object[] { shortcutPath }
                    );

                    if (shortcut != null)
                    {
                        Type scType = shortcut.GetType();
                        scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetExePath });
                        scType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(targetExePath) ?? "" });
                        scType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "DiskMonitor" });
                        scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetExePath + ",0" });
                        scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
                    }
                }
            }
        }
        catch { }
    }

    private static void DeleteStartupShortcut()
    {
        try
        {
            string shortcutPath = GetStartupShortcutPath();
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        catch { }
    }

    private bool IsStartWithWindowsEnabled()
    {
        try
        {
            // 1. Check Startup folder shortcut (most reliable in Windows 10/11)
            string shortcutPath = GetStartupShortcutPath();
            if (File.Exists(shortcutPath))
            {
                using var approvedFolderKey =
                    Registry.CurrentUser.OpenSubKey(
                        StartupApprovedFolder
                    );

                byte[]? folderApproved =
                    approvedFolderKey?.GetValue(
                        "DiskMonitor.lnk"
                    ) as byte[];

                if (folderApproved != null &&
                    folderApproved.Length > 0 &&
                    folderApproved[0] == 3)
                {
                    return false;
                }

                return true;
            }

            // 2. Also check HKCU Run key
            using var key =
                Registry.CurrentUser.OpenSubKey(
                    StartupRegistryPath
                );

            string? value =
                key?.GetValue(
                    StartupValueName
                ) as string;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            string registeredPath =
                value!.Trim().Trim('"');

            string currentPath =
                Application.ExecutablePath;

            bool pathMatches = string.Equals(
                Path.GetFullPath(
                    registeredPath
                ),
                Path.GetFullPath(
                    currentPath
                ),
                StringComparison.OrdinalIgnoreCase
            );

            if (!pathMatches)
                return false;

            using var approvedKey =
                Registry.CurrentUser.OpenSubKey(
                    StartupApprovedPath
                );

            byte[]? approved =
                approvedKey?.GetValue(
                    StartupValueName
                ) as byte[];

            if (approved != null &&
                approved.Length > 0 &&
                approved[0] == 3)
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshStartWithWindowsState()
    {
        if (startWithWindowsItem == null)
            return;

        startWithWindowsItem.Checked =
            IsStartWithWindowsEnabled();
    }

    private void ToggleStartWithWindows()
    {
        try
        {
            bool enable =
                !IsStartWithWindowsEnabled();

            using var key =
                Registry.CurrentUser.CreateSubKey(
                    StartupRegistryPath
                );

            if (enable)
            {
                // 1. Registry Run key
                key?.SetValue(
                    StartupValueName,
                    "\"" +
                    Application.ExecutablePath +
                    "\"",
                    RegistryValueKind.String
                );

                // 2. Shell Startup folder shortcut (.lnk)
                CreateStartupShortcut(Application.ExecutablePath);

                // 3. Set Task Manager approved state
                SetStartupApproved(true);
            }
            else
            {
                // 1. Delete Run key
                key?.DeleteValue(
                    StartupValueName,
                    false
                );

                // 2. Delete Startup shortcut
                DeleteStartupShortcut();

                // 3. Clear Task Manager state
                SetStartupApproved(false);
            }

            RefreshStartWithWindowsState();
        }
        catch
        {
            MessageBox.Show(
                "DiskMonitor could not change the Windows startup setting.",
                "DiskMonitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
    }

    // Synchronizes StartupApproved registry keys for both Run and StartupFolder
    // so Task Manager displays Enabled and Windows doesn't delay or skip startup.
    private void SetStartupApproved(bool enable)
    {
        try
        {
            byte[] data = new byte[12];
            data[0] = 2;

            using (var approvedRun = Registry.CurrentUser.CreateSubKey(StartupApprovedPath))
            {
                if (approvedRun != null)
                {
                    if (enable)
                        approvedRun.SetValue(StartupValueName, data, RegistryValueKind.Binary);
                    else
                        approvedRun.DeleteValue(StartupValueName, false);
                }
            }

            using (var approvedFolder = Registry.CurrentUser.CreateSubKey(StartupApprovedFolder))
            {
                if (approvedFolder != null)
                {
                    if (enable)
                        approvedFolder.SetValue("DiskMonitor.lnk", data, RegistryValueKind.Binary);
                    else
                        approvedFolder.DeleteValue("DiskMonitor.lnk", false);
                }
            }
        }
        catch { }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        try
        {
            PositionAtTopCenter();
            Invalidate();
        }
        catch { }
    }

    private void ToggleExpanded()
    {
        HideInfoPopup();

        expanded = !expanded;

        menu.Items[0].Text =
            expanded
                ? "Collapse"
                : "Expand";

        if (expanded)
            StartCollapseTimer();
        else
            StopCollapseTimer();

        StartSizeAnimation();
    }

    private Size GetTargetSize(bool isExpanded)
    {
        int count = Math.Max(drives.Count, 1);
        if (isExpanded)
        {
            return new Size(
                ExpandedWidth,
                (count * ExpandedRowHeight) + ExpandedFooterHeight
            );
        }
        else
        {
            int width = 178 + ((count - 1) * 118);
            return new Size(width, MiniHeight);
        }
    }

    private void StartSizeAnimation()
    {
        animStartSize = ClientSize;
        animTargetSize = GetTargetSize(expanded);

        if (animStartSize == animTargetSize)
            return;

        animCenterX = Location.X + (Width / 2);
        animTopY = Location.Y;
        animStartTime = DateTime.UtcNow;
        isAnimating = true;

        if (animTimer == null)
        {
            animTimer = new System.Windows.Forms.Timer { Interval = 15 };
            animTimer.Tick += OnAnimationTick;
        }

        animTimer.Stop();
        animTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - animStartTime).TotalMilliseconds;
        double t = Math.Min(1.0, elapsed / AnimDurationMs);

        // Quintic/Cubic Ease-Out for a buttery smooth deceleration landing
        double ease = 1.0 - Math.Pow(1.0 - t, 3);

        int curW = (int)Math.Round(animStartSize.Width + (animTargetSize.Width - animStartSize.Width) * ease);
        int curH = (int)Math.Round(animStartSize.Height + (animTargetSize.Height - animStartSize.Height) * ease);

        int curX = animCenterX - (curW / 2);

        SetBounds(curX, animTopY, curW, curH, BoundsSpecified.All);
        UpdateRoundedRegion();
        Invalidate();

        if (t >= 1.0)
        {
            animTimer?.Stop();
            isAnimating = false;

            int finalX = animCenterX - (animTargetSize.Width / 2);
            SetBounds(finalX, animTopY, animTargetSize.Width, animTargetSize.Height, BoundsSpecified.All);
            UpdateRoundedRegion();
            Invalidate();
        }
    }

    // Starts (or restarts) the auto-collapse timer.
    private void StartCollapseTimer()
    {
        if (collapseTimer == null)
        {
            collapseTimer =
                new System.Windows.Forms.Timer
                {
                    Interval =
                        AutoCollapseSeconds * 1000
                };

            collapseTimer.Tick += (_, _) =>
            {
                StopCollapseTimer();

                if (expanded)
                    ToggleExpanded();
            };
        }
        else
        {
            collapseTimer.Stop();
        }

        collapseTimer.Start();
    }

    // Stops and clears the auto-collapse timer.
    private void StopCollapseTimer()
    {
        collapseTimer?.Stop();
    }

    // Resets the collapse countdown whenever the
    // mouse moves over the expanded panel so the
    // user can read it without it disappearing.
    private void ResetCollapseTimerOnHover(
        object? sender,
        MouseEventArgs e)
    {
        if (expanded &&
            collapseTimer != null &&
            collapseTimer.Enabled)
        {
            collapseTimer.Stop();
            collapseTimer.Start();
        }
    }

    private void ToggleInfoPopup()
    {
        if (infoPopup != null &&
            !infoPopup.IsDisposed &&
            infoPopup.Visible)
        {
            infoPopup.Hide();
            return;
        }

        if (infoPopup == null ||
            infoPopup.IsDisposed)
        {
            infoPopup =
                new InfoPopup(
                    backgroundColor,
                    borderColor,
                    mutedColor,
                    blueColor
                );
        }

        PositionInfoPopup();

        infoPopup.TopMost = alwaysOnTop;
        infoPopup.Show(this);
        infoPopup.BringToFront();
    }

    private void HideInfoPopup()
    {
        if (infoPopup != null &&
            !infoPopup.IsDisposed)
        {
            infoPopup.Hide();
        }
    }

    private void PositionInfoPopup()
    {
        if (infoPopup == null ||
            infoPopup.IsDisposed)
            return;

        int x = Right + 7;

        int y =
            Top +
            (Height - infoPopup.Height) / 2;

        Rectangle workingArea =
            Screen
            .FromControl(this)
            .WorkingArea;

        // If there isn't enough space on the right,
        // place it on the left.
        if (x + infoPopup.Width >
            workingArea.Right)
        {
            x =
                Left -
                infoPopup.Width -
                7;
        }

        if (y < workingArea.Top)
            y = workingArea.Top + 4;

        if (y + infoPopup.Height >
            workingArea.Bottom)
        {
            y =
                workingArea.Bottom -
                infoPopup.Height -
                4;
        }

        infoPopup.Location =
            new Point(x, y);
    }

    private void ApplySize()
    {
        ClientSize = GetTargetSize(expanded);
        UpdateRoundedRegion();
    }

    private void PositionAtTopCenter()
    {
        var screen =
            Screen.PrimaryScreen;

        if (screen == null)
            return;

        var area =
            screen.WorkingArea;

        Location =
            new Point(
                area.Left +
                (area.Width - Width) / 2,
                area.Top + 8
            );
    }

    private void UpdateDrives()
    {
        try
        {
            drives.Clear();

            var foundDrives =
                DriveInfo
                .GetDrives()
                .Where(d =>
                    d.IsReady &&
                    d.DriveType ==
                    DriveType.Fixed)
                .OrderBy(d => d.Name);

            foreach (
                var drive in foundDrives)
            {
                long total =
                    drive.TotalSize;

                long free =
                    drive.AvailableFreeSpace;

                long used =
                    total - free;

                double percentage =
                    total > 0
                    ? (double)used /
                      total * 100
                    : 0;

                drives.Add(
                    new DriveStat
                    {
                        Name =
                            drive.Name
                            .TrimEnd('\\'),

                        Total = total,
                        Free = free,
                        Used = used,

                        Percentage =
                            percentage
                    }
                );
            }

            if (!isAnimating)
            {
                int oldWidth = Width;

                ApplySize();

                if (!expanded &&
                    oldWidth > 0 &&
                    oldWidth != Width)
                {
                    Location =
                        new Point(
                            Location.X -
                            ((Width - oldWidth) / 2),
                            Location.Y
                        );
                }

                PositionInfoPopup();
                Invalidate();
            }
        }
        catch
        {
            Invalidate();
        }
    }

    protected override void OnResize(
        EventArgs e)
    {
        base.OnResize(e);
        UpdateRoundedRegion();
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 ||
            Height <= 0)
            return;

        float radius = (Height > MiniHeight + 10) ? 17f : 14f;

        using var path =
            RoundedRectangle(
                new RectangleF(
                    0,
                    0,
                    Width,
                    Height
                ),
                radius
            );

        Region =
            new Region(path);
    }

    protected override void OnPaint(
        PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode =
            SmoothingMode.AntiAlias;

        e.Graphics.TextRenderingHint =
            System.Drawing.Text
            .TextRenderingHint
            .ClearTypeGridFit;

        e.Graphics.Clear(
            backgroundColor
        );

        if (Height > MiniHeight + 12)
            DrawExpanded(e.Graphics);
        else
            DrawMini(e.Graphics);
    }

    private Color GetUsageColor(
        double percentage)
    {
        return percentage >= 90
            ? warningColor
            : blueColor;
    }

    private void DrawMini(Graphics g)
    {
        RectangleF panel =
            new RectangleF(
                1,
                1,
                Width - 3,
                Height - 3
            );

        using (
            var borderPen =
                new Pen(
                    borderColor,
                    1.4f))
        {
            using var path =
                RoundedRectangle(
                    panel,
                    13
                );

            g.DrawPath(
                borderPen,
                path
            );
        }

        // Information button on the right.
        infoButtonRect =
            new RectangleF(
                Width - 32,
                10,
                23,
                23
            );

        DrawInfoButton(
            g,
            infoButtonRect
        );

        if (drives.Count == 0)
        {
            using var font =
                new Font(
                    "Segoe UI",
                    12f,
                    FontStyle.Regular,
                    GraphicsUnit.Pixel
                );

            using var brush =
                new SolidBrush(
                    mutedColor
                );

            g.DrawString(
                "No drives",
                font,
                brush,
                18,
                12
            );

            return;
        }

        float x = 12;

        using var driveFont =
            new Font(
                "Segoe UI Semibold",
                9.5f,
                FontStyle.Bold
            );

        using var percentFont =
            new Font(
                "Segoe UI",
                9.5f,
                FontStyle.Regular
            );

        using var white =
            new SolidBrush(
                Color.White
            );

        for (
            int i = 0;
            i < drives.Count;
            i++)
        {
            var drive =
                drives[i];

            Color usageColor =
                GetUsageColor(
                    drive.Percentage
                );

            using var usageBrush =
                new SolidBrush(
                    usageColor
                );

            DrawMiniDriveIcon(
                g,
                new RectangleF(
                    x,
                    9,
                    22,
                    22
                )
            );

            float textX =
                x + 33;

            g.DrawString(
                drive.Name,
                driveFont,
                white,
                new PointF(
                    textX,
                    7
                )
            );

            SizeF driveSize =
                g.MeasureString(
                    drive.Name,
                    driveFont
                );

            float percentageX =
                textX +
                driveSize.Width +
                7;

            g.DrawString(
                $"{drive.Percentage:0}%",
                percentFont,
                usageBrush,
                new PointF(
                    percentageX,
                    7
                )
            );

            // Progress bar deliberately lower
            // than the text.
            float availableRight =
                i == drives.Count - 1
                    ? infoButtonRect.Left - 12
                    : x + 108;

            float barWidth =
                Math.Max(
                    54,
                    availableRight -
                    textX
                );

            RectangleF barBack =
                new RectangleF(
                    textX,
                    33,
                    barWidth,
                    3
                );

            DrawRoundedFill(
                g,
                barBack,
                progressBackground,
                1.5f
            );

            float filled =
                barBack.Width *
                (float)(
                    drive.Percentage /
                    100.0
                );

            if (filled > 1)
            {
                DrawRoundedFill(
                    g,
                    new RectangleF(
                        barBack.X,
                        barBack.Y,
                        filled,
                        barBack.Height
                    ),
                    usageColor,
                    1.5f
                );
            }

            x += 118;
        }
    }

    private void DrawInfoButton(
        Graphics g,
        RectangleF rect)
    {
        using var circleFill =
            new SolidBrush(
                Color.FromArgb(
                    17,
                    36,
                    55
                )
            );

        using var circleBorder =
            new Pen(
                Color.FromArgb(
                    72,
                    111,
                    145
                ),
                1.2f
            );

        g.FillEllipse(
            circleFill,
            rect
        );

        g.DrawEllipse(
            circleBorder,
            rect
        );

        using var infoFont =
            new Font(
                "Segoe UI Semibold",
                10.5f,
                FontStyle.Bold
            );

        using var infoBrush =
            new SolidBrush(
                Color.FromArgb(
                    143,
                    201,
                    255
                )
            );

        string text = "i";

        SizeF size =
            g.MeasureString(
                text,
                infoFont
            );

        g.DrawString(
            text,
            infoFont,
            infoBrush,
            new PointF(
                rect.X +
                ((rect.Width -
                  size.Width) / 2),

                rect.Y +
                ((rect.Height -
                  size.Height) / 2) -
                1
            )
        );
    }

    private void DrawExpanded(Graphics g)
    {
        RectangleF panel =
            new RectangleF(
                1.5f,
                1.5f,
                Width - 4,
                Height - 4
            );

        using (
            var borderPen =
                new Pen(
                    borderColor,
                    1.7f))
        {
            using var path =
                RoundedRectangle(
                    panel,
                    16
                );

            g.DrawPath(
                borderPen,
                path
            );
        }

        int footerTop = Height - ExpandedFooterHeight;

        if (drives.Count > 0)
        {
            for (
                int i = 0;
                i < drives.Count;
                i++)
            {
                int rowY = 5 + (i * ExpandedRowHeight);
                if (rowY + 15 < footerTop)
                {
                    var clipState = g.Save();
                    g.SetClip(new RectangleF(0, rowY, Width, Math.Min(ExpandedRowHeight, Math.Max(0, footerTop - rowY))));
                    DrawExpandedDrive(
                        g,
                        drives[i],
                        rowY
                    );
                    g.Restore(clipState);
                }
            }
        }

        if (footerTop >= 15)
        {
            var footerState = g.Save();
            g.SetClip(new RectangleF(0, footerTop, Width, ExpandedFooterHeight));
            DrawExpandedFooter(g);
            g.Restore(footerState);
        }
    }

    private void DrawExpandedDrive(
        Graphics g,
        DriveStat drive,
        int top)
    {
        Color usageColor =
            GetUsageColor(
                drive.Percentage
            );

        using var usageBrush =
            new SolidBrush(
                usageColor
            );

        RectangleF iconBox =
            new RectangleF(
                16,
                top + 11,
                48,
                48
            );

        DrawRoundedFill(
            g,
            iconBox,
            Color.FromArgb(
                17,
                34,
                51
            ),
            11
        );

        using (
            var iconBorder =
                new Pen(
                    Color.FromArgb(
                        44,
                        67,
                        89
                    ),
                    1.2f))
        {
            using var p =
                RoundedRectangle(
                    iconBox,
                    11
                );

            g.DrawPath(
                iconBorder,
                p
            );
        }

        DrawLargeDriveIcon(
            g,
            iconBox
        );

        using var driveFont =
            new Font(
                "Segoe UI Semibold",
                15.5f,
                FontStyle.Bold
            );

        using var percentFont =
            new Font(
                "Segoe UI",
                15.5f,
                FontStyle.Regular
            );

        using var usedFont =
            new Font(
                "Segoe UI",
                10.5f,
                FontStyle.Regular
            );

        using var storageFont =
            new Font(
                "Segoe UI",
                10.5f,
                FontStyle.Regular
            );

        using var white =
            new SolidBrush(
                Color.White
            );

        using var muted =
            new SolidBrush(
                mutedColor
            );

        float textY =
            top + 10;

        g.DrawString(
            drive.Name,
            driveFont,
            white,
            new PointF(
                82,
                textY
            )
        );

        g.DrawString(
            $"{drive.Percentage:0}%",
            percentFont,
            usageBrush,
            new PointF(
                126,
                textY
            )
        );

        g.DrawString(
            "used",
            usedFont,
            muted,
            new PointF(
                195,
                textY + 5
            )
        );

        string storageText =
            $"{FormatBytes(drive.Free)} free / {FormatBytes(drive.Total)}";

        SizeF storageSize =
            g.MeasureString(
                storageText,
                storageFont
            );

        // Right-align to 18 px from the edge,
        // but never overlap the "used" label.
        float storageX =
            Math.Max(
                240,
                Width - storageSize.Width - 18
            );

        g.DrawString(
            storageText,
            storageFont,
            muted,
            new PointF(
                storageX,
                textY + 5
            )
        );

        RectangleF barBack =
            new RectangleF(
                82,
                top + 55,
                Width - 100,
                9
            );

        DrawRoundedFill(
            g,
            barBack,
            progressBackground,
            4.5f
        );

        float progressWidth =
            barBack.Width *
            (float)(
                drive.Percentage /
                100.0
            );

        if (progressWidth > 2)
        {
            DrawRoundedFill(
                g,
                new RectangleF(
                    barBack.X,
                    barBack.Y,
                    progressWidth,
                    barBack.Height
                ),
                usageColor,
                4.5f
            );
        }
    }

    private void DrawExpandedFooter(
        Graphics g)
    {
        int footerTop =
            Height -
            ExpandedFooterHeight;

        float footerCenterY =
            footerTop +
            (ExpandedFooterHeight / 2f);

        using var separator =
            new Pen(
                Color.FromArgb(
                    28,
                    50,
                    69
                ),
                1
            );

        g.DrawLine(
            separator,
            16,
            footerTop,
            Width - 16,
            footerTop
        );

        using var footerFont =
            new Font(
                "Segoe UI",
                8.5f,
                FontStyle.Regular
            );

        using var footerStrongFont =
            new Font(
                "Segoe UI Semibold",
                8.5f,
                FontStyle.Bold
            );

        using var muted =
            new SolidBrush(
                Color.FromArgb(
                    112,
                    143,
                    174
                )
            );

        string maker =
            "Made by " + DeveloperName;

        string product =
            AppName;

        string version =
            $"v{AppVersion}";

        SizeF makerSize =
            g.MeasureString(
                maker,
                footerFont
            );

        SizeF productSize =
            g.MeasureString(
                product,
                footerStrongFont
            );

        SizeF versionSize =
            g.MeasureString(
                version,
                footerFont
            );

        float textHeight =
            Math.Max(
                makerSize.Height,
                Math.Max(
                    productSize.Height,
                    versionSize.Height
                )
            );

        float textY =
            footerCenterY -
            (textHeight / 2f) -
            1;

        // Left: Made by DgLogiQ
        g.DrawString(
            maker,
            footerFont,
            muted,
            new PointF(
                18,
                textY
            )
        );

        // Right: v1.02
        float versionX =
            Width -
            versionSize.Width -
            18;

        g.DrawString(
            version,
            footerFont,
            muted,
            new PointF(
                versionX,
                textY
            )
        );

        // Perfectly centered divider
        float dividerX =
            versionX - 13;

        using var dividerPen =
            new Pen(
                Color.FromArgb(
                    93,
                    129,
                    161
                ),
                1
            );

        float dividerHeight = 15;

        g.DrawLine(
            dividerPen,
            dividerX,
            footerCenterY -
            (dividerHeight / 2f),

            dividerX,
            footerCenterY +
            (dividerHeight / 2f)
        );

        // DiskMonitor before divider
        g.DrawString(
            product,
            footerStrongFont,
            muted,
            new PointF(
                dividerX -
                productSize.Width -
                13,

                textY
            )
        );
    }

    private void DrawMiniDriveIcon(
        Graphics g,
        RectangleF bounds)
    {
        RectangleF body =
            new RectangleF(
                bounds.X + 3,
                bounds.Y + 4,
                bounds.Width - 6,
                bounds.Height - 7
            );

        using var gradient =
            new LinearGradientBrush(
                body,
                Color.FromArgb(
                    220,
                    232,
                    242
                ),
                Color.FromArgb(
                    91,
                    116,
                    139
                ),
                LinearGradientMode.Vertical
            );

        using var path =
            RoundedRectangle(
                body,
                3
            );

        g.FillPath(
            gradient,
            path
        );

        using var baseBrush =
            new SolidBrush(
                Color.FromArgb(
                    38,
                    59,
                    78
                )
            );

        g.FillRectangle(
            baseBrush,
            body.X + 1,
            body.Bottom - 5,
            body.Width - 2,
            4
        );

        using var led =
            new SolidBrush(
                blueColor
            );

        g.FillEllipse(
            led,
            body.X + 4,
            body.Bottom - 3.5f,
            2,
            2
        );
    }

    private void DrawLargeDriveIcon(
        Graphics g,
        RectangleF container)
    {
        RectangleF body =
            new RectangleF(
                container.X + 13,
                container.Y + 12,
                22,
                27
            );

        using var gradient =
            new LinearGradientBrush(
                body,
                Color.FromArgb(
                    226,
                    236,
                    244
                ),
                Color.FromArgb(
                    91,
                    117,
                    141
                ),
                LinearGradientMode.Vertical
            );

        using var bodyPath =
            RoundedRectangle(
                body,
                4
            );

        g.FillPath(
            gradient,
            bodyPath
        );

        RectangleF bottom =
            new RectangleF(
                body.X + 1,
                body.Bottom - 7,
                body.Width - 2,
                6
            );

        using var bottomBrush =
            new SolidBrush(
                Color.FromArgb(
                    39,
                    61,
                    82
                )
            );

        using var bottomPath =
            RoundedRectangle(
                bottom,
                2
            );

        g.FillPath(
            bottomBrush,
            bottomPath
        );

        using var led =
            new SolidBrush(
                blueColor
            );

        g.FillEllipse(
            led,
            bottom.X + 4,
            bottom.Y + 2,
            3,
            3
        );
    }

    private static void DrawRoundedFill(
        Graphics g,
        RectangleF rectangle,
        Color color,
        float radius)
    {
        if (rectangle.Width <= 0 ||
            rectangle.Height <= 0)
            return;

        float safeRadius =
            Math.Min(
                radius,
                Math.Min(
                    rectangle.Width / 2,
                    rectangle.Height / 2
                )
            );

        using var path =
            RoundedRectangle(
                rectangle,
                safeRadius
            );

        using var brush =
            new SolidBrush(
                color
            );

        g.FillPath(
            brush,
            path
        );
    }

    internal static GraphicsPath RoundedRectangle(
        RectangleF rect,
        float radius)
    {
        GraphicsPath path =
            new GraphicsPath();

        float diameter =
            radius * 2;

        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        RectangleF arc =
            new RectangleF(
                rect.X,
                rect.Y,
                diameter,
                diameter
            );

        path.AddArc(
            arc,
            180,
            90
        );

        arc.X =
            rect.Right - diameter;

        path.AddArc(
            arc,
            270,
            90
        );

        arc.Y =
            rect.Bottom - diameter;

        path.AddArc(
            arc,
            0,
            90
        );

        arc.X =
            rect.Left;

        path.AddArc(
            arc,
            90,
            90
        );

        path.CloseFigure();

        return path;
    }

    private static string FormatBytes(
        long bytes)
    {
        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;
        const double TB = GB * 1024;

        if (bytes >= TB)
            return
                $"{bytes / TB:0.##} TB";

        if (bytes >= GB)
            return
                $"{bytes / GB:0.#} GB";

        if (bytes >= MB)
            return
                $"{bytes / MB:0.#} MB";

        return
            $"{bytes / KB:0.#} KB";
    }

    private sealed class DriveStat
    {
        public string Name
        {
            get;
            set;
        } = "";

        public long Total
        {
            get;
            set;
        }

        public long Free
        {
            get;
            set;
        }

        public long Used
        {
            get;
            set;
        }

        public double Percentage
        {
            get;
            set;
        }
    }

    private sealed class InfoPopup : Form
    {
        private readonly Color bg;
        private readonly Color border;
        private readonly Color muted;
        private readonly Color accent;

        public InfoPopup(
            Color background,
            Color borderColor,
            Color mutedColor,
            Color accentColor)
        {
            bg = background;
            border = borderColor;
            muted = mutedColor;
            accent = accentColor;

            FormBorderStyle =
                FormBorderStyle.None;

            StartPosition =
                FormStartPosition.Manual;

            ShowInTaskbar = false;
            TopMost = true;

            BackColor = bg;

            ClientSize =
                new Size(
                    190,
                    108
                );

            DoubleBuffered = true;

            AutoScaleMode =
                AutoScaleMode.None;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true
            );

            UpdateShape();
        }

        protected override bool ShowWithoutActivation
            => true;

        protected override void OnPaint(
            PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode =
                SmoothingMode.AntiAlias;

            e.Graphics.TextRenderingHint =
                System.Drawing.Text
                .TextRenderingHint
                .ClearTypeGridFit;

            e.Graphics.Clear(bg);

            // Outer border
            using var borderPen =
                new Pen(
                    Color.FromArgb(
                        66,
                        104,
                        138
                    ),
                    1.2f
                );

            using var bodyPath =
                MainForm.RoundedRectangle(
                    new RectangleF(
                        1,
                        1,
                        Width - 3,
                        Height - 3
                    ),
                    14
                );

            e.Graphics.DrawPath(
                borderPen,
                bodyPath
            );

            // Typography hierarchy
            using var titleFont =
                new Font(
                    "Segoe UI Semibold",
                    12.5f,
                    FontStyle.Bold
                );

            using var byFont =
                new Font(
                    "Segoe UI",
                    8.5f,
                    FontStyle.Regular
                );

            using var brandFont =
                new Font(
                    "Segoe UI Semibold",
                    9.5f,
                    FontStyle.Bold
                );

            using var versionFont =
                new Font(
                    "Segoe UI Semibold",
                    8f,
                    FontStyle.Bold
                );

            using var titleBrush =
                new SolidBrush(
                    Color.White
                );

            using var secondaryBrush =
                new SolidBrush(
                    Color.FromArgb(
                        119,
                        149,
                        180
                    )
                );

            using var brandBrush =
                new SolidBrush(
                    Color.FromArgb(
                        180,
                        208,
                        231
                    )
                );

            // -------------------------------------------------
            // Title
            // -------------------------------------------------

            e.Graphics.DrawString(
                AppName,
                titleFont,
                titleBrush,
                new PointF(
                    17,
                    12
                )
            );

            // -------------------------------------------------
            // Company line
            // More breathing space below title
            // -------------------------------------------------

            float companyY = 48;

            e.Graphics.DrawString(
                "by",
                byFont,
                secondaryBrush,
                new PointF(
                    18,
                    companyY
                )
            );

            SizeF bySize =
                e.Graphics.MeasureString(
                    "by",
                    byFont
                );

            e.Graphics.DrawString(
                DeveloperName,
                brandFont,
                brandBrush,
                new PointF(
                    18 +
                    bySize.Width +
                    5,
                    companyY - 1
                )
            );

            // -------------------------------------------------
            // Version badge
            // -------------------------------------------------

            RectangleF versionBadge =
                new RectangleF(
                    18,
                    78,
                    58,
                    19
                );

            using var badgePath =
                MainForm.RoundedRectangle(
                    versionBadge,
                    7
                );

            using var badgeFill =
                new SolidBrush(
                    Color.FromArgb(
                        15,
                        36,
                        54
                    )
                );

            using var badgeBorder =
                new Pen(
                    Color.FromArgb(
                        54,
                        82,
                        107
                    ),
                    1
                );

            e.Graphics.FillPath(
                badgeFill,
                badgePath
            );

            e.Graphics.DrawPath(
                badgeBorder,
                badgePath
            );

            string versionText =
                $"v{AppVersion}";

            SizeF versionSize =
                e.Graphics.MeasureString(
                    versionText,
                    versionFont
                );

            e.Graphics.DrawString(
                versionText,
                versionFont,
                secondaryBrush,
                new PointF(
                    versionBadge.X +
                    ((versionBadge.Width -
                      versionSize.Width) / 2),

                    versionBadge.Y +
                    ((versionBadge.Height -
                      versionSize.Height) / 2) -
                    1
                )
            );
        }

        private void UpdateShape()
        {
            using var path =
                MainForm.RoundedRectangle(
                    new RectangleF(
                        0,
                        0,
                        Width,
                        Height
                    ),
                    15
                );

            Region =
                new Region(path);
        }
    }
}









