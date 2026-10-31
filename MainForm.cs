using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DiskMonitor;

public class MainForm : Form
{
    private const string AppName = "DiskMonitor";
    private const string DeveloperName = "DgLogiQ";
    private const string AppVersion = "1.07";

    private const string ScheduledTaskName =
        "DiskMonitor";

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
    private ToolStripMenuItem powerTimerCancelItem = null!;
    private readonly List<DriveStat> drives = new();

    private bool alwaysOnTop = true;
    private bool expanded;
    private bool mouseHeld;
    private bool dragging;
    private bool infoPressed;
    private bool powerTimerPressed;

    private Point dragStartCursor;
    private Point dragStartWindow;

    private RectangleF infoButtonRect;

    private InfoPopup? infoPopup;

    private const int MiniHeight = 44;
    private const int ExpandedWidth = 520;

    // Drive area + footer.
    private const int ExpandedRowHeight = 80;
    private const int ExpandedFooterHeight = 40;

    // Power Timer section layout constants
    private const int PowerTimerHeaderHeight = 38;
    private const int PowerTimerContentHeight = 152;
    private const int PowerTimerWarningSeconds = 30;

    // Power Timer state
    private bool powerTimerSectionExpanded;
    private bool powerTimerActive;
    private int powerTimerAction; // 0=Shutdown, 1=Sleep, 2=Hibernate
    private int powerTimerSelectedMinutes = 30;
    private DateTime powerTimerEndTime;
    private System.Windows.Forms.Timer? powerTimerTickTimer;
    private PowerTimerWarningForm? powerTimerWarning;
    private bool powerTimerWarningShown;
    private TextBox? customMinutesBox;

    // Power Timer hit-test regions (set during paint)
    private RectangleF ptHeaderRect;
    private readonly RectangleF[] ptActionRects = new RectangleF[3];
    private readonly RectangleF[] ptPresetRects = new RectangleF[4];
    private RectangleF ptCustomBoxRect;
    private RectangleF ptStartBtnRect;
    private RectangleF ptCancelBtnRect;

    private static readonly string[] PowerActionNames = { "Shutdown", "Sleep", "Hibernate" };
    private static readonly string[] PowerActionIcons = { "\u23FB", "\u23FE", "\u2B55" };
    private static readonly int[] PresetMinutes = { 15, 30, 45, 60 };

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

    private readonly Color accentGreen =
        Color.FromArgb(0, 200, 120);

    private readonly Color yellowColor =
        Color.FromArgb(255, 200, 0);

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

        // Power Timer submenu
        var powerTimerMenu = new ToolStripMenuItem("Power Timer");
        powerTimerMenu.DropDownItems.Add("Shutdown in 15 min", null, (_, _) => QuickPowerTimer(0, 15));
        powerTimerMenu.DropDownItems.Add("Shutdown in 30 min", null, (_, _) => QuickPowerTimer(0, 30));
        powerTimerMenu.DropDownItems.Add("Sleep in 15 min", null, (_, _) => QuickPowerTimer(1, 15));
        powerTimerMenu.DropDownItems.Add("Sleep in 30 min", null, (_, _) => QuickPowerTimer(1, 30));
        powerTimerMenu.DropDownItems.Add(new ToolStripSeparator());
        powerTimerMenu.DropDownItems.Add("Custom...", null, (_, _) =>
        {
            if (!expanded) ToggleExpanded();
            powerTimerSectionExpanded = true;
            StartSizeAnimation();
        });
        powerTimerMenu.DropDownItems.Add(new ToolStripSeparator());
        powerTimerCancelItem = new ToolStripMenuItem("Cancel Timer", null, (_, _) => CancelPowerTimer());
        powerTimerCancelItem.Visible = false;
        powerTimerMenu.DropDownItems.Add(powerTimerCancelItem);
        menu.Items.Add(powerTimerMenu);
        menu.Opening += (_, _) => { powerTimerCancelItem.Visible = powerTimerActive; };

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
        MouseMove += (_, e) => UpdateCursor(e.Location);
        MouseLeave += (_, _) => Cursor = Cursors.Default;
        MouseUp += EndMouseAction;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Shown += (_, _) =>
        {
            UpdateDrives();
            ApplySize();
            PositionAtTopCenter();
            RefreshAlwaysOnTopState();
            RefreshStartWithWindowsState();

            // Triple-layer autostart synchronization:
            // If startup is enabled, ensure Scheduled Task, Startup Shortcut, and Run key are all registered
            if (IsStartWithWindowsEnabled())
            {
                CreateStartupTask(Application.ExecutablePath);
                CreateStartupShortcut(Application.ExecutablePath);
                SetStartupApproved(true);
                try
                {
                    using var rk = Registry.CurrentUser.CreateSubKey(StartupRegistryPath);
                    rk?.SetValue(StartupValueName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                }
                catch { }
            }

            refreshTimer.Start();

            // Startup stabilization timer:
            // At boot/logon, display drivers and the taskbar may resize or take 1-3 seconds to settle.
            // Re-assert positioning and topmost state so the widget never vanishes or clips offscreen.
            var bootTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            int bootTicks = 0;
            bootTimer.Tick += (_, _) =>
            {
                bootTicks++;
                PositionAtTopCenter();
                TopMost = alwaysOnTop;
                BringToFront();
                if (bootTicks >= 3)
                {
                    bootTimer.Stop();
                    bootTimer.Dispose();
                }
            };
            bootTimer.Start();
        };

        FormClosed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            animTimer?.Stop();
            animTimer?.Dispose();

            collapseTimer?.Stop();
            collapseTimer?.Dispose();

            powerTimerTickTimer?.Stop();
            powerTimerTickTimer?.Dispose();

            if (infoPopup != null &&
                !infoPopup.IsDisposed)
            {
                infoPopup.Close();
            }

            if (powerTimerWarning != null &&
                !powerTimerWarning.IsDisposed)
            {
                powerTimerWarning.Close();
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

    // ─────────────────────────────────────────────
    // Power Timer Logic
    // ─────────────────────────────────────────────

    private void QuickPowerTimer(int action, int minutes)
    {
        powerTimerAction = action;
        powerTimerSelectedMinutes = minutes;
        if (customMinutesBox != null)
            customMinutesBox.Text = minutes.ToString();
        StartPowerTimer();
    }

    private void StartPowerTimer()
    {
        if (powerTimerSelectedMinutes <= 0)
            return;

        powerTimerActive = true;
        powerTimerWarningShown = false;
        powerTimerEndTime = DateTime.UtcNow.AddMinutes(powerTimerSelectedMinutes);
        powerTimerCancelItem.Visible = true;

        if (customMinutesBox != null)
            customMinutesBox.Visible = false;

        if (powerTimerTickTimer == null)
        {
            powerTimerTickTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            powerTimerTickTimer.Tick += OnPowerTimerTick;
        }

        powerTimerTickTimer.Start();

        // Save last used action preference
        SavePowerTimerPreference();

        if (!expanded)
        {
            StartSizeAnimation();
        }
        else
        {
            Invalidate();

            // Auto-collapse after 1.5 seconds of activating the timer
            var startCollapseDelay = new System.Windows.Forms.Timer { Interval = 1500 };
            startCollapseDelay.Tick += (_, _) =>
            {
                startCollapseDelay.Stop();
                startCollapseDelay.Dispose();
                if (expanded && powerTimerActive)
                {
                    ToggleExpanded();
                }
            };
            startCollapseDelay.Start();
        }
    }

    private void CancelPowerTimer()
    {
        powerTimerActive = false;
        powerTimerWarningShown = false;
        powerTimerTickTimer?.Stop();
        powerTimerCancelItem.Visible = false;

        if (powerTimerWarning != null &&
            !powerTimerWarning.IsDisposed)
        {
            powerTimerWarning.Close();
            powerTimerWarning = null;
        }

        if (!expanded)
        {
            StartSizeAnimation();
        }
        else
        {
            if (powerTimerSectionExpanded)
                EnsureCustomMinutesBox();
            Invalidate();
        }
    }

    private void OnPowerTimerTick(object? sender, EventArgs e)
    {
        if (!powerTimerActive) return;

        TimeSpan remaining = powerTimerEndTime - DateTime.UtcNow;

        if (remaining.TotalSeconds <= 0)
        {
            // Execute the action
            powerTimerTickTimer?.Stop();
            powerTimerActive = false;
            powerTimerCancelItem.Visible = false;

            if (powerTimerWarning != null &&
                !powerTimerWarning.IsDisposed)
            {
                powerTimerWarning.Close();
                powerTimerWarning = null;
            }

            Invalidate();
            ExecutePowerAction();
            return;
        }

        // Show warning at 30 seconds
        if (remaining.TotalSeconds <= PowerTimerWarningSeconds && !powerTimerWarningShown)
        {
            powerTimerWarningShown = true;
            ShowPowerTimerWarning();
        }

        // Update warning countdown if visible
        if (powerTimerWarning != null &&
            !powerTimerWarning.IsDisposed &&
            powerTimerWarning.Visible)
        {
            powerTimerWarning.UpdateCountdown((int)Math.Ceiling(remaining.TotalSeconds));
        }

        Invalidate();
    }

    private void ShowPowerTimerWarning()
    {
        try { SystemSounds.Exclamation.Play(); }
        catch { }

        if (powerTimerWarning != null && !powerTimerWarning.IsDisposed)
            powerTimerWarning.Close();

        powerTimerWarning = new PowerTimerWarningForm(
            PowerActionNames[powerTimerAction],
            backgroundColor, borderColor, mutedColor, warningColor, blueColor
        );

        powerTimerWarning.CancelRequested += (_, _) => CancelPowerTimer();
        powerTimerWarning.ProceedRequested += (_, _) =>
        {
            powerTimerEndTime = DateTime.UtcNow; // trigger immediate execution on next tick
        };

        // Position above/near the main form
        var screen = Screen.PrimaryScreen;
        if (screen != null)
        {
            var area = screen.WorkingArea;
            powerTimerWarning.Location = new Point(
                area.Left + (area.Width - powerTimerWarning.Width) / 2,
                area.Top + (area.Height - powerTimerWarning.Height) / 2
            );
        }

        powerTimerWarning.TopMost = true;
        powerTimerWarning.Show();
        powerTimerWarning.BringToFront();
    }

    private void ExecutePowerAction()
    {
        try
        {
            switch (powerTimerAction)
            {
                case 0: // Shutdown
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown",
                        Arguments = "/s /t 0",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    break;

                case 1: // Sleep
                    Application.SetSuspendState(PowerState.Suspend, false, false);
                    break;

                case 2: // Hibernate
                    Application.SetSuspendState(PowerState.Hibernate, false, false);
                    break;
            }
        }
        catch { }
    }

    private void SavePowerTimerPreference()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath);
            key?.SetValue("LastPowerAction", powerTimerAction, RegistryValueKind.DWord);
        }
        catch { }
    }

    private void LoadPowerTimerPreference()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
            if (key != null)
            {
                object? val = key.GetValue("LastPowerAction");
                if (val is int intVal && intVal >= 0 && intVal <= 2)
                    powerTimerAction = intVal;
            }
        }
        catch { }
    }

    private TimeSpan GetPowerTimerRemaining()
    {
        if (!powerTimerActive) return TimeSpan.Zero;
        var rem = powerTimerEndTime - DateTime.UtcNow;
        return rem.TotalSeconds > 0 ? rem : TimeSpan.Zero;
    }

    private string FormatCountdown(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private Color GetCountdownColor(TimeSpan remaining)
    {
        if (remaining.TotalSeconds <= 60) return warningColor;
        if (remaining.TotalMinutes <= 5) return yellowColor;
        return Color.White;
    }

    // ─────────────────────────────────────────────
    // Mouse Handling
    // ─────────────────────────────────────────────

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

        // Check power timer hit regions in expanded view
        if (expanded && HandlePowerTimerClick(e.Location))
        {
            powerTimerPressed = true;
            return;
        }

        infoPressed = false;
        powerTimerPressed = false;
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

        if (powerTimerPressed)
        {
            powerTimerPressed = false;
            return;
        }

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

    private bool HandlePowerTimerClick(Point location)
    {
        // Power timer header click — toggle section
        if (ptHeaderRect.Contains(location))
        {
            powerTimerSectionExpanded = !powerTimerSectionExpanded;

            if (!powerTimerSectionExpanded && customMinutesBox != null)
                customMinutesBox.Visible = false;

            StartSizeAnimation();

            if (powerTimerSectionExpanded)
            {
                if (!powerTimerActive)
                    EnsureCustomMinutesBox();
                StartCollapseTimer(); // reset auto-collapse
            }

            return true;
        }

        if (!powerTimerSectionExpanded) return false;

        // Action buttons (Shutdown / Sleep / Hibernate)
        for (int i = 0; i < 3; i++)
        {
            if (ptActionRects[i].Contains(location) && !powerTimerActive)
            {
                powerTimerAction = i;
                StartCollapseTimer();
                Invalidate();
                return true;
            }
        }

        // Preset buttons (15, 30, 45, 60)
        for (int i = 0; i < PresetMinutes.Length; i++)
        {
            if (ptPresetRects[i].Contains(location) && !powerTimerActive)
            {
                powerTimerSelectedMinutes = PresetMinutes[i];
                if (customMinutesBox != null)
                    customMinutesBox.Text = powerTimerSelectedMinutes.ToString();
                StartCollapseTimer();
                Invalidate();
                return true;
            }
        }

        // Start button
        if (ptStartBtnRect.Contains(location) && !powerTimerActive)
        {
            // Read custom box value
            if (customMinutesBox != null && int.TryParse(customMinutesBox.Text, out int mins) && mins > 0)
                powerTimerSelectedMinutes = mins;

            StartPowerTimer();
            StartCollapseTimer();
            return true;
        }

        // Cancel button
        if (ptCancelBtnRect.Contains(location) && powerTimerActive)
        {
            CancelPowerTimer();
            StartCollapseTimer();
            return true;
        }

        // Click inside section — consume but don't toggle expanded
        float ptSectionTop = ptHeaderRect.Bottom;
        if (location.Y >= ptSectionTop && location.Y <= ptSectionTop + PowerTimerContentHeight)
        {
            StartCollapseTimer();
            return true;
        }

        return false;
    }

    private void UpdateCursor(Point location)
    {
        if (mouseHeld || dragging)
        {
            Cursor = Cursors.Default;
            return;
        }

        if (!expanded)
        {
            if (!powerTimerActive && infoButtonRect.Contains(location))
                Cursor = Cursors.Hand;
            else
                Cursor = Cursors.Default;
            return;
        }

        if (ptHeaderRect.Contains(location))
        {
            Cursor = Cursors.Hand;
            return;
        }

        if (powerTimerSectionExpanded)
        {
            if (!powerTimerActive)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (ptActionRects[i].Contains(location)) { Cursor = Cursors.Hand; return; }
                }
                for (int i = 0; i < PresetMinutes.Length; i++)
                {
                    if (ptPresetRects[i].Contains(location)) { Cursor = Cursors.Hand; return; }
                }
                if (ptStartBtnRect.Contains(location)) { Cursor = Cursors.Hand; return; }
            }
            else
            {
                if (ptCancelBtnRect.Contains(location)) { Cursor = Cursors.Hand; return; }
            }
        }

        Cursor = Cursors.Default;
    }

    private void EnsureCustomMinutesBox()
    {
        if (customMinutesBox != null && !customMinutesBox.IsDisposed)
        {
            customMinutesBox.Visible = true;
            return;
        }

        customMinutesBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(17, 36, 55),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            TextAlign = HorizontalAlignment.Center,
            MaxLength = 4,
            Text = powerTimerSelectedMinutes.ToString()
        };

        customMinutesBox.KeyPress += (_, ke) =>
        {
            if (!char.IsDigit(ke.KeyChar) && ke.KeyChar != (char)8) // allow backspace
                ke.Handled = true;
        };

        customMinutesBox.KeyDown += (_, ke) =>
        {
            if (ke.KeyCode == Keys.Enter)
            {
                ke.SuppressKeyPress = true;
                if (int.TryParse(customMinutesBox.Text, out int m) && m > 0)
                {
                    powerTimerSelectedMinutes = m;
                    StartPowerTimer();
                    StartCollapseTimer();
                }
            }
        };

        customMinutesBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(customMinutesBox.Text, out int v) && v > 0)
            {
                powerTimerSelectedMinutes = v;
                Invalidate();
            }
        };

        customMinutesBox.GotFocus += (_, _) => customMinutesBox.SelectAll();
        customMinutesBox.MouseClick += (_, _) => customMinutesBox.SelectAll();

        Controls.Add(customMinutesBox);
        customMinutesBox.BringToFront();
    }

    private void PositionCustomMinutesBox()
    {
        if (customMinutesBox == null || customMinutesBox.IsDisposed) return;

        if (!expanded || !powerTimerSectionExpanded || powerTimerActive)
        {
            customMinutesBox.Visible = false;
            return;
        }

        customMinutesBox.Visible = true;
        customMinutesBox.Location = new Point(
            (int)ptCustomBoxRect.X + 4,
            (int)ptCustomBoxRect.Y + 3
        );
        customMinutesBox.Size = new Size(
            (int)ptCustomBoxRect.Width - 8,
            (int)ptCustomBoxRect.Height - 6
        );
    }

    // ─────────────────────────────────────────────
    // Settings persistence
    // ─────────────────────────────────────────────

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

    // Creates a logon-triggered Windows Scheduled Task for DiskMonitor.
    // Scheduled tasks bypass Windows 11 startup serialization and run reliably on logon.
    private static void CreateStartupTask(string targetExePath)
    {
        try
        {
            Type? schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) return;

            dynamic service = Activator.CreateInstance(schedulerType);
            service.Connect();
            dynamic folder = service.GetFolder(@"\");

            dynamic taskDef = service.NewTask(0);
            taskDef.RegistrationInfo.Description = "DiskMonitor Startup Task";
            taskDef.Settings.DisallowStartIfOnBatteries = false;
            taskDef.Settings.StopIfGoingOnBatteries = false;
            taskDef.Settings.ExecutionTimeLimit = "PT0S";
            taskDef.Settings.Priority = 4;

            // TASK_TRIGGER_LOGON = 9
            dynamic trigger = taskDef.Triggers.Create(9);
            trigger.UserId = Environment.UserDomainName + "\\" + Environment.UserName;

            // TASK_ACTION_EXEC = 0
            dynamic action = taskDef.Actions.Create(0);
            action.Path = targetExePath;
            action.WorkingDirectory = Path.GetDirectoryName(targetExePath) ?? "";

            // TASK_CREATE_OR_UPDATE = 6, TASK_LOGON_INTERACTIVE_TOKEN = 3
            folder.RegisterTaskDefinition(ScheduledTaskName, taskDef, 6, null, null, 3, null);
        }
        catch { }
    }

    private static void DeleteStartupTask()
    {
        try
        {
            Type? schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) return;

            dynamic service = Activator.CreateInstance(schedulerType);
            service.Connect();
            dynamic folder = service.GetFolder(@"\");
            folder.DeleteTask(ScheduledTaskName, 0);
        }
        catch { }
    }

    private static bool IsStartupTaskRegistered()
    {
        try
        {
            Type? schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) return false;

            dynamic service = Activator.CreateInstance(schedulerType);
            service.Connect();
            dynamic folder = service.GetFolder(@"\");
            dynamic task = folder.GetTask(ScheduledTaskName);
            if (task == null) return false;
            return (bool)task.Enabled;
        }
        catch
        {
            return false;
        }
    }

    private bool IsStartWithWindowsEnabled()
    {
        try
        {
            // 1. Check Windows Scheduled Task (gold-standard on Windows 11)
            if (IsStartupTaskRegistered())
                return true;

            // 2. Check Startup folder shortcut (shell:startup)
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

            // 3. Also check HKCU Run key
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
                // 1. Windows Scheduled Task (runs on logon with highest reliability)
                CreateStartupTask(Application.ExecutablePath);

                // 2. Shell Startup folder shortcut (.lnk)
                CreateStartupShortcut(Application.ExecutablePath);

                // 3. Registry Run key
                key?.SetValue(
                    StartupValueName,
                    "\"" +
                    Application.ExecutablePath +
                    "\"",
                    RegistryValueKind.String
                );

                // 4. Set Task Manager approved state
                SetStartupApproved(true);
            }
            else
            {
                // 1. Delete Scheduled Task
                DeleteStartupTask();

                // 2. Delete Startup shortcut
                DeleteStartupShortcut();

                // 3. Delete Run key
                key?.DeleteValue(
                    StartupValueName,
                    false
                );

                // 4. Clear Task Manager state
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
            BeginInvoke(new Action(() =>
            {
                ApplySize();
                PositionAtTopCenter();
                TopMost = alwaysOnTop;
                BringToFront();
                Invalidate();
            }));
        }
        catch { }
    }

    // ─────────────────────────────────────────────
    // Expand / Collapse & Animation
    // ─────────────────────────────────────────────

    private void ToggleExpanded()
    {
        HideInfoPopup();

        expanded = !expanded;

        menu.Items[0].Text =
            expanded
                ? "Collapse"
                : "Expand";

        if (!expanded)
        {
            powerTimerSectionExpanded = false;
            if (customMinutesBox != null)
                customMinutesBox.Visible = false;
        }
        else
        {
            // Auto-expand power timer section when timer is active
            if (powerTimerActive)
                powerTimerSectionExpanded = true;

            // Load preference when expanding
            LoadPowerTimerPreference();
        }

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
            int height = (count * ExpandedRowHeight) + ExpandedFooterHeight;

            // Add power timer header
            height += PowerTimerHeaderHeight;

            // Add power timer content if section is expanded
            if (powerTimerSectionExpanded)
                height += PowerTimerContentHeight;

            return new Size(ExpandedWidth, height);
        }
        else
        {
            int width = 178 + ((count - 1) * 118);

            // When timer is active, ensure enough width for countdown text
            if (powerTimerActive)
                width = Math.Max(width, 230);

            return new Size(width, MiniHeight);
        }
    }

    private void StartSizeAnimation()
    {
        if (customMinutesBox != null)
            customMinutesBox.Visible = false;

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
            PositionCustomMinutesBox();
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
                // Do not auto-collapse while actively configuring Power Timer settings or typing
                if (!powerTimerActive && (powerTimerSectionExpanded || (customMinutesBox != null && customMinutesBox.Focused)))
                {
                    StartCollapseTimer();
                    return;
                }

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

        if (area.Width <= 0 || area.Height <= 0)
            return;

        Size currentOrTarget = (Width > 0 && Height > 0) ? Size : GetTargetSize(expanded);
        int targetWidth = currentOrTarget.Width;
        int targetHeight = currentOrTarget.Height;

        int x = area.Left + Math.Max(0, (area.Width - targetWidth) / 2);
        int y = area.Top + 8;

        // Ensure window is strictly within visible bounds of primary screen
        if (x + targetWidth > area.Right)
            x = Math.Max(area.Left, area.Right - targetWidth);
        if (x < area.Left)
            x = area.Left;

        if (y + targetHeight > area.Bottom)
            y = Math.Max(area.Top, area.Bottom - targetHeight);
        if (y < area.Top)
            y = area.Top;

        Location = new Point(x, y);
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

    // ─────────────────────────────────────────────
    // Painting
    // ─────────────────────────────────────────────

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

        // If power timer is active, show countdown instead of drives
        if (powerTimerActive)
        {
            DrawMiniCountdown(g);
            return;
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

    private void DrawMiniCountdown(Graphics g)
    {
        TimeSpan remaining = GetPowerTimerRemaining();
        Color countdownColor = GetCountdownColor(remaining);
        string actionName = PowerActionNames[powerTimerAction];
        string countdown = FormatCountdown(remaining);

        using var iconFont = new Font("Segoe UI", 12.5f, FontStyle.Regular);
        using var actionFont = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        using var timerFont = new Font("Consolas", 12.5f, FontStyle.Bold);
        using var actionBrush = new SolidBrush(mutedColor);
        using var timerBrush = new SolidBrush(countdownColor);

        // Power icon
        string icon = "\u23FB"; // ⏻
        SizeF iconSize = g.MeasureString(icon, iconFont);
        SizeF nameSize = g.MeasureString(actionName, actionFont);
        SizeF timerSize = g.MeasureString(countdown, timerFont);

        float totalW = iconSize.Width + 4 + nameSize.Width + 8 + timerSize.Width;
        float startX = Math.Max(12, (Width - totalW) / 2f);

        g.DrawString(icon, iconFont, timerBrush, new PointF(startX, (MiniHeight - iconSize.Height) / 2f));

        // Action name
        float nameX = startX + iconSize.Width + 4;
        g.DrawString(actionName, actionFont, actionBrush, new PointF(nameX, (MiniHeight - nameSize.Height) / 2f));

        // Countdown
        float timerX = nameX + nameSize.Width + 8;
        g.DrawString(countdown, timerFont, timerBrush, new PointF(timerX, (MiniHeight - timerSize.Height) / 2f));
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

        int driveAreaBottom = 5 + Math.Max(drives.Count, 1) * ExpandedRowHeight;
        int footerTop = Height - ExpandedFooterHeight;

        if (drives.Count > 0)
        {
            for (
                int i = 0;
                i < drives.Count;
                i++)
            {
                int rowY = 5 + (i * ExpandedRowHeight);
                if (rowY + 15 < driveAreaBottom)
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

        // Draw Power Timer section between drives and footer
        int ptTop = driveAreaBottom;
        if (footerTop > ptTop)
        {
            var ptState = g.Save();
            g.SetClip(new RectangleF(0, ptTop, Width, footerTop - ptTop));
            DrawPowerTimerSection(g, ptTop);
            g.Restore(ptState);
        }

        if (footerTop >= 15)
        {
            var footerState = g.Save();
            g.SetClip(new RectangleF(0, footerTop, Width, ExpandedFooterHeight));
            DrawExpandedFooter(g);
            g.Restore(footerState);
        }
    }

    // ─────────────────────────────────────────────
    // Power Timer Drawing (Expanded View)
    // ─────────────────────────────────────────────

    private void DrawPowerTimerSection(Graphics g, int top)
    {
        // Separator line
        using var separator = new Pen(Color.FromArgb(28, 50, 69), 1);
        g.DrawLine(separator, 16, top, Width - 16, top);

        // Header row
        ptHeaderRect = new RectangleF(8, top + 2, Width - 16, PowerTimerHeaderHeight - 4);

        using var headerFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        using var headerBrush = new SolidBrush(mutedColor);
        using var chevronFont = new Font("Segoe UI", 9f, FontStyle.Regular);

        // Power icon + label
        string headerText = "\u23FB  Power Timer";

        // If timer is active, show countdown in header too
        if (powerTimerActive)
        {
            TimeSpan rem = GetPowerTimerRemaining();
            Color cc = GetCountdownColor(rem);
            string activeText = $"\u23FB  {PowerActionNames[powerTimerAction]} in {FormatCountdown(rem)}";
            using var activeBrush = new SolidBrush(cc);
            g.DrawString(activeText, headerFont, activeBrush, new PointF(18, top + 9));
        }
        else
        {
            g.DrawString(headerText, headerFont, headerBrush, new PointF(18, top + 9));
        }

        // Chevron
        string chevron = powerTimerSectionExpanded ? "\u25B4" : "\u25BE"; // ▴ or ▾
        SizeF chevSize = g.MeasureString(chevron, chevronFont);
        g.DrawString(chevron, chevronFont, headerBrush,
            new PointF(Width - 30, top + (PowerTimerHeaderHeight - chevSize.Height) / 2f));

        if (!powerTimerSectionExpanded) return;

        // Content area
        int contentTop = top + PowerTimerHeaderHeight;

        if (powerTimerActive)
        {
            DrawPowerTimerActiveContent(g, contentTop);
        }
        else
        {
            DrawPowerTimerSetupContent(g, contentTop);
        }
    }

    private void DrawPowerTimerSetupContent(Graphics g, int top)
    {
        using var labelFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var btnFont = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(mutedColor);
        using var whiteBrush = new SolidBrush(Color.White);

        int y = top + 8;

        // Action buttons row
        float btnWidth = 130;
        float btnHeight = 30;
        float gap = 10;
        float totalWidth = btnWidth * 3 + gap * 2;
        float startX = (Width - totalWidth) / 2f;

        for (int i = 0; i < 3; i++)
        {
            float bx = startX + i * (btnWidth + gap);
            ptActionRects[i] = new RectangleF(bx, y, btnWidth, btnHeight);

            bool selected = (powerTimerAction == i);
            Color bgCol = selected ? Color.FromArgb(0, 100, 180) : Color.FromArgb(17, 36, 55);
            Color borderCol = selected ? Color.FromArgb(0, 145, 255) : Color.FromArgb(44, 67, 89);

            DrawRoundedFill(g, ptActionRects[i], bgCol, 8);
            using var btnBorder = new Pen(borderCol, 1.2f);
            using var btnPath = RoundedRectangle(ptActionRects[i], 8);
            g.DrawPath(btnBorder, btnPath);

            string label = PowerActionNames[i];
            SizeF labelSize = g.MeasureString(label, btnFont);
            g.DrawString(label, btnFont, selected ? whiteBrush : labelBrush,
                new PointF(bx + (btnWidth - labelSize.Width) / 2f, y + (btnHeight - labelSize.Height) / 2f));
        }

        y += (int)btnHeight + 12;

        // Timer preset row
        float presetBtnW = 48;
        float presetBtnH = 30;
        float presetGap = 8;
        float presetTotalW = presetBtnW * 4 + presetGap * 3;
        float customBoxW = 70;
        float minLabelW = 30;
        float rowTotalW = presetTotalW + 14 + customBoxW + 6 + minLabelW;
        float presetStartX = (Width - rowTotalW) / 2f;

        for (int i = 0; i < PresetMinutes.Length; i++)
        {
            float px = presetStartX + i * (presetBtnW + presetGap);
            ptPresetRects[i] = new RectangleF(px, y, presetBtnW, presetBtnH);

            bool selected = (powerTimerSelectedMinutes == PresetMinutes[i]);
            Color bgCol = selected ? Color.FromArgb(0, 80, 140) : Color.FromArgb(17, 36, 55);
            Color borderCol = selected ? blueColor : Color.FromArgb(44, 67, 89);

            DrawRoundedFill(g, ptPresetRects[i], bgCol, 7);
            using var pBorder = new Pen(borderCol, 1f);
            using var pPath = RoundedRectangle(ptPresetRects[i], 7);
            g.DrawPath(pBorder, pPath);

            string pLabel = PresetMinutes[i].ToString();
            SizeF pSize = g.MeasureString(pLabel, btnFont);
            g.DrawString(pLabel, btnFont, selected ? whiteBrush : labelBrush,
                new PointF(px + (presetBtnW - pSize.Width) / 2f, y + (presetBtnH - pSize.Height) / 2f));
        }

        // Custom input box area
        float customX = presetStartX + presetTotalW + 14;
        ptCustomBoxRect = new RectangleF(customX, y, customBoxW, presetBtnH);
        DrawRoundedFill(g, ptCustomBoxRect, Color.FromArgb(17, 36, 55), 7);
        using var cBorder = new Pen(Color.FromArgb(44, 67, 89), 1f);
        using var cPath = RoundedRectangle(ptCustomBoxRect, 7);
        g.DrawPath(cBorder, cPath);

        // "min" label
        g.DrawString("min", labelFont, labelBrush, new PointF(customX + customBoxW + 6, y + 7));

        PositionCustomMinutesBox();

        y += (int)presetBtnH + 14;

        // Start button
        float startBtnW = 180;
        float startBtnH = 34;
        float startBtnX = (Width - startBtnW) / 2f;
        ptStartBtnRect = new RectangleF(startBtnX, y, startBtnW, startBtnH);

        DrawRoundedFill(g, ptStartBtnRect, Color.FromArgb(0, 120, 200), 9);
        using var startBorder = new Pen(Color.FromArgb(0, 160, 255), 1.2f);
        using var startPath = RoundedRectangle(ptStartBtnRect, 9);
        g.DrawPath(startBorder, startPath);

        string startLabel = "\u25B6  Start Timer";
        using var startFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        SizeF startSize = g.MeasureString(startLabel, startFont);
        g.DrawString(startLabel, startFont, whiteBrush,
            new PointF(startBtnX + (startBtnW - startSize.Width) / 2f, y + (startBtnH - startSize.Height) / 2f));
    }

    private void DrawPowerTimerActiveContent(Graphics g, int top)
    {
        TimeSpan remaining = GetPowerTimerRemaining();
        Color countdownColor = GetCountdownColor(remaining);

        using var actionFont = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold);
        using var timerFont = new Font("Consolas", 28f, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var actionBrush = new SolidBrush(mutedColor);
        using var timerBrush = new SolidBrush(countdownColor);
        using var whiteBrush = new SolidBrush(Color.White);

        // Action label
        string actionLabel = $"{PowerActionNames[powerTimerAction]} in";
        SizeF actionSize = g.MeasureString(actionLabel, actionFont);
        g.DrawString(actionLabel, actionFont, actionBrush,
            new PointF((Width - actionSize.Width) / 2f, top + 8));

        // Large countdown
        string countdown = FormatCountdown(remaining);
        SizeF countdownSize = g.MeasureString(countdown, timerFont);
        g.DrawString(countdown, timerFont, timerBrush,
            new PointF((Width - countdownSize.Width) / 2f, top + 28));

        // Scheduled time label
        DateTime localEnd = powerTimerEndTime.ToLocalTime();
        string schedLabel = $"Scheduled at {localEnd:h:mm tt}";
        SizeF schedSize = g.MeasureString(schedLabel, labelFont);
        using var schedBrush = new SolidBrush(Color.FromArgb(100, 130, 160));
        g.DrawString(schedLabel, labelFont, schedBrush,
            new PointF((Width - schedSize.Width) / 2f, top + 74));

        // Cancel button
        float cancelBtnW = 160;
        float cancelBtnH = 32;
        float cancelBtnX = (Width - cancelBtnW) / 2f;
        float cancelBtnY = top + 104;
        ptCancelBtnRect = new RectangleF(cancelBtnX, cancelBtnY, cancelBtnW, cancelBtnH);

        DrawRoundedFill(g, ptCancelBtnRect, Color.FromArgb(60, 20, 20), 9);
        using var cancelBorder = new Pen(Color.FromArgb(180, 50, 50), 1.2f);
        using var cancelPath = RoundedRectangle(ptCancelBtnRect, 9);
        g.DrawPath(cancelBorder, cancelPath);

        string cancelLabel = "\u2715  Cancel Timer";
        using var cancelFont = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
        SizeF cancelSize = g.MeasureString(cancelLabel, cancelFont);
        using var cancelTextBrush = new SolidBrush(warningColor);
        g.DrawString(cancelLabel, cancelFont, cancelTextBrush,
            new PointF(cancelBtnX + (cancelBtnW - cancelSize.Width) / 2f,
                       cancelBtnY + (cancelBtnH - cancelSize.Height) / 2f));
    }

    // ─────────────────────────────────────────────
    // Drive Drawing (existing, preserved)
    // ─────────────────────────────────────────────

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

        // Right: v1.07
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

    // ─────────────────────────────────────────────
    // Icon Drawing Helpers (existing, preserved)
    // ─────────────────────────────────────────────

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

    // ─────────────────────────────────────────────
    // Geometry Helpers (existing, preserved)
    // ─────────────────────────────────────────────

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

    // ─────────────────────────────────────────────
    // Inner Classes
    // ─────────────────────────────────────────────

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

            // Title
            e.Graphics.DrawString(
                AppName,
                titleFont,
                titleBrush,
                new PointF(
                    17,
                    12
                )
            );

            // Company line
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

            // Version badge
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

    // ─────────────────────────────────────────────
    // Power Timer Warning Popup
    // ─────────────────────────────────────────────

    private sealed class PowerTimerWarningForm : Form
    {
        private readonly string actionName;
        private readonly Color bg;
        private readonly Color border;
        private readonly Color muted;
        private readonly Color warning;
        private readonly Color accent;
        private int countdown;

        private RectangleF cancelBtnRect;
        private RectangleF proceedBtnRect;

        public event EventHandler? CancelRequested;
        public event EventHandler? ProceedRequested;

        public PowerTimerWarningForm(
            string action,
            Color background,
            Color borderColor,
            Color mutedColor,
            Color warningColor,
            Color accentColor)
        {
            actionName = action;
            bg = background;
            border = borderColor;
            muted = mutedColor;
            warning = warningColor;
            accent = accentColor;
            countdown = PowerTimerWarningSeconds;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = bg;
            ClientSize = new Size(360, 170);
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true
            );

            MouseClick += OnMouseClick;
            UpdateShape();
        }

        public void UpdateCountdown(int seconds)
        {
            countdown = seconds;
            Invalidate();
        }

        protected override bool ShowWithoutActivation => false; // This one SHOULD activate

        private void OnMouseClick(object? sender, MouseEventArgs e)
        {
            if (cancelBtnRect.Contains(e.Location))
                CancelRequested?.Invoke(this, EventArgs.Empty);
            else if (proceedBtnRect.Contains(e.Location))
                ProceedRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(bg);

            // Border
            using var borderPen = new Pen(warning, 1.8f);
            using var bodyPath = MainForm.RoundedRectangle(new RectangleF(1, 1, Width - 3, Height - 3), 16);
            g.DrawPath(borderPen, bodyPath);

            using var titleFont = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
            using var subtitleFont = new Font("Segoe UI", 10.5f, FontStyle.Regular);
            using var countdownFont = new Font("Consolas", 18f, FontStyle.Bold);
            using var btnFont = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
            using var whiteBrush = new SolidBrush(Color.White);
            using var warningBrush = new SolidBrush(warning);
            using var mutedBrush = new SolidBrush(muted);

            // Warning icon + title
            string title = $"\u26A0  System {actionName} in {countdown}s";
            SizeF titleSize = g.MeasureString(title, titleFont);
            g.DrawString(title, titleFont, warningBrush, new PointF((Width - titleSize.Width) / 2f, 20));

            // Subtitle
            string sub = "Save your work!";
            SizeF subSize = g.MeasureString(sub, subtitleFont);
            g.DrawString(sub, subtitleFont, mutedBrush, new PointF((Width - subSize.Width) / 2f, 55));

            // Progress bar showing remaining time
            float barX = 30;
            float barY = 85;
            float barW = Width - 60;
            float barH = 5;
            float progress = Math.Max(0, Math.Min(1f, countdown / (float)PowerTimerWarningSeconds));

            MainForm.DrawRoundedFill(g, new RectangleF(barX, barY, barW, barH), Color.FromArgb(31, 52, 72), 2.5f);
            if (progress > 0.01f)
                MainForm.DrawRoundedFill(g, new RectangleF(barX, barY, barW * progress, barH), warning, 2.5f);

            // Buttons
            float btnW = 130;
            float btnH = 34;
            float btnY = 105;
            float gap = 20;

            cancelBtnRect = new RectangleF(Width / 2f - btnW - gap / 2f, btnY, btnW, btnH);
            proceedBtnRect = new RectangleF(Width / 2f + gap / 2f, btnY, btnW, btnH);

            // Cancel button
            MainForm.DrawRoundedFill(g, cancelBtnRect, Color.FromArgb(17, 36, 55), 9);
            using var cBorder = new Pen(Color.FromArgb(44, 67, 89), 1.2f);
            using var cPath = MainForm.RoundedRectangle(cancelBtnRect, 9);
            g.DrawPath(cBorder, cPath);
            string cLabel = "Cancel";
            SizeF cSize = g.MeasureString(cLabel, btnFont);
            g.DrawString(cLabel, btnFont, whiteBrush,
                new PointF(cancelBtnRect.X + (btnW - cSize.Width) / 2f, btnY + (btnH - cSize.Height) / 2f));

            // Proceed button
            MainForm.DrawRoundedFill(g, proceedBtnRect, Color.FromArgb(140, 30, 30), 9);
            using var pBorder = new Pen(warning, 1.2f);
            using var pPath = MainForm.RoundedRectangle(proceedBtnRect, 9);
            g.DrawPath(pBorder, pPath);
            string pLabel = "Proceed Now";
            SizeF pSize = g.MeasureString(pLabel, btnFont);
            g.DrawString(pLabel, btnFont, whiteBrush,
                new PointF(proceedBtnRect.X + (btnW - pSize.Width) / 2f, btnY + (btnH - pSize.Height) / 2f));
        }

        private void UpdateShape()
        {
            using var path = MainForm.RoundedRectangle(new RectangleF(0, 0, Width, Height), 16);
            Region = new Region(path);
        }
    }
}
