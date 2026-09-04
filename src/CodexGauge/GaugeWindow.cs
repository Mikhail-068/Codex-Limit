using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexGauge;

public sealed class GaugeWindow : Window
{
    public GaugeFace Face { get; } = new();
    private readonly CodexClient client = new();
    private readonly Preferences settings;
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer freshness = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Forms.NotifyIcon? tray;
    private List<QuotaWindow> windows = new();
    private DateTimeOffset? updated;
    private bool busy, closed, error, authRequired;
    private string errorText = "Подключение…";
    private readonly bool diagnostic;
    public int SuccessfulRefreshes { get; private set; }
    public DateTimeOffset? LastUpdate => updated;

    public GaugeWindow(bool diagnostic = false)
    {
        this.diagnostic = diagnostic;
        settings = diagnostic ? new Preferences() : Preferences.Load();
        Title = "Codex Gauge"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false;
        Topmost = settings.Topmost; Width = Height = double.IsFinite(settings.Size) ? Math.Clamp(settings.Size, 147, 294) : 200;
        Content = Face;
        var area = SystemParameters.WorkArea;
        Left = double.IsFinite(settings.Left) ? settings.Left : area.Right - Width - 24;
        Top = double.IsFinite(settings.Top) ? settings.Top : area.Bottom - Height - 24;
        // Recover a saved window from a disconnected monitor.
        var virtualArea = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!virtualArea.IntersectsWith(new Rect(Left, Top, Width, Height))) { Left = area.Right - Width - 24; Top = area.Bottom - Height - 24; }
        Face.ToolTip = "Перетащите прибор мышью. Правый клик — меню. Двойной клик — размер.";
        Face.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ResizeWidget(Width < 190 ? 200 : 160);
            else { try { DragMove(); Save(); } catch (InvalidOperationException) { } }
        };
        Face.MouseRightButtonUp += (_, _) => { var menu = CreateMenu(); menu.PlacementTarget = Face; menu.IsOpen = true; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); if (e.Key == Key.F5) _ = RefreshAsync(); };
        if (!diagnostic)
        {
            tray = new Forms.NotifyIcon { Text = "Codex Gauge · подключение", Icon = MakeTrayIcon(), Visible = true };
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Показать виджет", null, (_, _) => Dispatcher.Invoke(Restore));
            menu.Items.Add("Обновить", null, (_, _) => Dispatcher.Invoke(() => { _ = RefreshAsync(); }));
            menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(Close));
            tray.ContextMenuStrip = menu;
            Loaded += (_, _) => StartMonitoring();
        }
        poll.Tick += async (_, _) => await RefreshAsync();
        freshness.Tick += (_, _) => UpdateStatus();
        client.LimitsChanged += () => Dispatcher.BeginInvoke(new Action(() => { if (!closed) _ = RefreshAsync(); }));
        Closed += (_, _) => { closed = true; poll.Stop(); freshness.Stop(); tray?.Dispose(); client.Dispose(); if (!diagnostic) Save(); };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    private static System.Drawing.Icon MakeTrayIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);
        using var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(16, 27, 38));
        g.FillEllipse(bg, 1, 1, 30, 30);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(99, 230, 155), 2);
        g.DrawRectangle(pen, 9, 7, 10, 17); g.DrawRectangle(pen, 12, 10, 4, 4);
        g.DrawLine(pen, 7, 25, 21, 25); g.DrawLines(pen, new[] { new System.Drawing.Point(19, 16), new System.Drawing.Point(23, 16), new System.Drawing.Point(23, 23), new System.Drawing.Point(26, 23), new System.Drawing.Point(26, 12), new System.Drawing.Point(22, 8) });
        var h = bitmap.GetHicon();
        try { using var original = System.Drawing.Icon.FromHandle(h); return (System.Drawing.Icon)original.Clone(); }
        finally { DestroyIcon(h); }
    }

    private void Save()
    {
        if (diagnostic) return;
        settings.Left = Left; settings.Top = Top; settings.Size = Width; settings.Topmost = Topmost; settings.SizeVersion = 1;
        if (!settings.Save()) Face.ToolTip = "Не удалось сохранить расположение виджета. Проверьте доступ к папке настроек.";
    }
    private void Restore() { Show(); Activate(); }
    public void StartMonitoring() { poll.Start(); freshness.Start(); _ = RefreshAsync(); }
    private void ResizeWidget(double size) { Width = Height = size; Save(); }
    private static MenuItem Item(string label, Action action, bool? check = null)
    {
        var item = new MenuItem { Header = label };
        if (check.HasValue) { item.IsCheckable = true; item.IsChecked = check.Value; }
        item.Click += (_, _) => action(); return item;
    }
    internal ContextMenu CreateMenu()
    {
        var menu = new ContextMenu();
        menu.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("MenuTheme.xaml", UriKind.Relative) });
        menu.Items.Add(new MenuItem { Header = "CODEX GAUGE  ·  каждые 10 сек", IsEnabled = false });
        menu.Items.Add(new Separator());
        foreach (var w in windows)
            menu.Items.Add(Item(w.MenuLabel, () => { settings.SelectedKey = w.Key; SelectQuota(); Save(); }, Face.Quota?.Key == w.Key));
        if (windows.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(Item("Обновить сейчас", () => { _ = RefreshAsync(); }));
        if (authRequired) menu.Items.Add(Item("Войти в Codex…", () => { _ = LoginAsync(); }));
        menu.Items.Add(Item("Поверх всех окон", () => { Topmost = !Topmost; Save(); }, Topmost));
        var sizes = new MenuItem { Header = "Размер" };
        foreach (var (label, size) in new[] { ("Компактный · 160", 160d), ("Обычный · 200", 200d), ("Крупный · 253", 253d) })
            sizes.Items.Add(Item(label, () => ResizeWidget(size), Math.Abs(Width - size) < 1));
        menu.Items.Add(sizes);
        menu.Items.Add(Item("Скрыть в трей", Hide));
        menu.Items.Add(new Separator()); menu.Items.Add(Item("Выход", Close));
        return menu;
    }

    public async System.Threading.Tasks.Task RefreshAsync()
    {
        if (busy || closed) return;
        busy = true;
        try
        {
            var result = await client.ReadLimitsAsync();
            if (closed) return;
            windows = QuotaParser.Parse(result); updated = DateTimeOffset.UtcNow; SuccessfulRefreshes++;
            error = false; authRequired = false; SelectQuota();
        }
        catch (Exception e)
        {
            if (closed) return;
            error = true; authRequired = e is CodexException { AuthenticationRequired: true };
            errorText = authRequired ? "Нужен вход · меню" : e is System.IO.FileNotFoundException ? "Codex не найден" : e is TimeoutException ? "Нет ответа Codex" : "Нет связи с Codex";
            if (authRequired) { windows.Clear(); Face.Quota = null; Face.UpdateValue(false); }
        }
        finally { busy = false; if (!closed) UpdateStatus(); }
    }
    private void SelectQuota()
    {
        Face.Quota = QuotaParser.Select(windows, settings.SelectedKey); Face.UpdateValue();
    }
    private async System.Threading.Tasks.Task LoginAsync()
    {
        if (busy || closed) return;
        busy = true;
        try
        {
            var url = await client.LoginAsync();
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            errorText = "Войдите в браузере";
        }
        catch { errorText = "Не удалось войти"; }
        finally { busy = false; UpdateStatus(); }
    }
    private void UpdateStatus()
    {
        var age = updated.HasValue ? (DateTimeOffset.UtcNow - updated.Value).TotalSeconds : double.PositiveInfinity;
        var resetPassed = Face.Quota?.Reset is { } reset && reset <= DateTimeOffset.UtcNow;
        Face.Fresh = !error && age < 25 && !resetPassed && Face.Quota != null;
        Face.Status = error ? errorText : !updated.HasValue ? "Подключение…" : Face.Quota == null ? "Лимиты недоступны" : resetPassed ? "Ожидаем сброс" : age >= 25 ? "Данные устарели" : $"Обновлено {Math.Max(0, (int)age)} с назад";
        var timestamp = updated?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "ещё не было";
        Face.ToolTip = (Face.Quota?.MenuLabel ?? "Нет показаний") + "\nПоследнее обновление: " + timestamp + "\nПроверка каждые 10 секунд. Правый клик — меню.\nПеретащите мышью; двойной клик меняет размер.";
        if (tray != null)
        {
            var label = Face.Quota == null ? "Codex · " + Face.Status : $"Codex · {Face.Quota.Remaining:0.#}% · {Face.Quota.Period}" + (Face.Fresh ? "" : " · устарело");
            tray.Text = label.Length > 63 ? label[..63] : label;
        }
        Face.InvalidateVisual();
    }
}
