using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace HistoryClipboard.App.Windows;

public sealed partial class TrayIconController : IDisposable
{
    private readonly Forms.ContextMenuStrip contextMenu;
    private readonly Action exitApplication;
    private readonly Forms.ToolStripMenuItem exitItem;
    private readonly Drawing.Icon icon;
    private readonly Forms.ToolStripMenuItem openItem;
    private readonly Action prepareForTrayInteraction;
    private readonly Action showWindow;
    private readonly Forms.NotifyIcon trayIcon;
    private readonly Action toggleWindow;
    private bool isDisposed;

    public TrayIconController(
        Action prepareForTrayInteraction,
        Action showWindow,
        Action toggleWindow,
        Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(prepareForTrayInteraction);
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(toggleWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        this.prepareForTrayInteraction = prepareForTrayInteraction;
        this.showWindow = showWindow;
        this.toggleWindow = toggleWindow;
        this.exitApplication = exitApplication;

        icon = CreateTrayIcon();
        openItem = new Forms.ToolStripMenuItem("显示历史记录");
        exitItem = new Forms.ToolStripMenuItem("退出");
        openItem.Click += OnOpenClicked;
        exitItem.Click += OnExitClicked;

        contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = contextMenu,
            Icon = icon,
            Text = "历史粘贴板",
            Visible = true,
        };
        trayIcon.MouseDown += OnTrayIconMouseDown;
        trayIcon.MouseClick += OnTrayIconMouseClick;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        trayIcon.Visible = false;
        trayIcon.MouseDown -= OnTrayIconMouseDown;
        trayIcon.MouseClick -= OnTrayIconMouseClick;
        openItem.Click -= OnOpenClicked;
        exitItem.Click -= OnExitClicked;
        trayIcon.Dispose();
        contextMenu.Dispose();
        icon.Dispose();
    }

    public void Republish()
    {
        if (isDisposed)
        {
            return;
        }

        trayIcon.Visible = false;
        trayIcon.Visible = true;
    }

    private void OnTrayIconMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            toggleWindow();
        }
    }

    private void OnTrayIconMouseDown(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            prepareForTrayInteraction();
        }
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        showWindow();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        exitApplication();
    }

    private static Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(Drawing.Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var backBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 126, 211, 238));
        using var frontBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 36, 112, 168));
        using var linePen = new Drawing.Pen(Drawing.Color.FromArgb(235, 240, 250, 255), 2);
        graphics.FillRectangle(backBrush, 5, 3, 20, 23);
        graphics.FillRectangle(frontBrush, 9, 7, 19, 22);
        graphics.DrawLine(linePen, 13, 13, 24, 13);
        graphics.DrawLine(linePen, 13, 18, 24, 18);
        graphics.DrawLine(linePen, 13, 23, 21, 23);

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var sourceIcon = Drawing.Icon.FromHandle(iconHandle);
            return (Drawing.Icon)sourceIcon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(iconHandle);
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyIcon(IntPtr iconHandle);
    }
}
