using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodexGauge;

public sealed class GaugeFace : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeFace), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public QuotaWindow? Quota { get; set; }
    public string Status { get; set; } = "Подключение…";
    public bool Fresh { get; set; }
    public bool Preview { get; set; }
    private static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static readonly Brush White = Brush("#F2F7FB"), Muted = Brush("#94A6B8"), Green = Brush("#63E69B"), Yellow = Brush("#F5C95E"), Red = Brush("#FF727B");
    public static string FuelZone(double remaining) => remaining < 10 ? "red" : remaining <= 20 ? "yellow" : "green";
    private static Brush FuelColor(double remaining) => FuelZone(remaining) switch { "red" => Red, "yellow" => Yellow, _ => Green };
    private static Point At(double value, double radius) { var a = (140 + value * 2.6) * Math.PI / 180; return new Point(170 + Math.Cos(a) * radius, 168 + Math.Sin(a) * radius); }
    public void UpdateValue(bool animate = true)
    {
        var target = Quota?.Remaining ?? 0;
        if (animate && Quota != null) BeginAnimation(ValueProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(650)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else { BeginAnimation(ValueProperty, null); Value = target; }
        InvalidateVisual();
    }
    private static void Arc(DrawingContext dc, double start, double end, double radius, Brush color, double width)
    {
        if (end <= start) return;
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(At(start, radius), false, false); c.ArcTo(At(end, radius), new Size(radius, radius), 0, (end - start) * 2.6 > 180, SweepDirection.Clockwise, true, false); }
        dc.DrawGeometry(null, new Pen(color, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
    }
    private void Text(DrawingContext dc, string text, double x, double y, double size, Brush color, bool bold = false)
    {
        var ft = new FormattedText(text, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(x - ft.Width / 2, y));
    }
    private static void FuelPump(DrawingContext dc, double x, double y, Brush color)
    {
        var pen = new Pen(color, 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawRoundedRectangle(null, pen, new Rect(x, y, 17, 27), 2, 2);
        dc.DrawRoundedRectangle(color, null, new Rect(x + 4, y + 4, 9, 8), 1, 1);
        dc.DrawLine(pen, new Point(x - 3, y + 28), new Point(x + 20, y + 28));
        var hose = new StreamGeometry();
        using (var c = hose.Open())
        {
            c.BeginFigure(new Point(x + 17, y + 15), false, false);
            c.LineTo(new Point(x + 22, y + 15), true, false);
            c.LineTo(new Point(x + 22, y + 23), true, false);
            c.BezierTo(new Point(x + 22, y + 28), new Point(x + 29, y + 28), new Point(x + 29, y + 22), true, false);
            c.LineTo(new Point(x + 29, y + 8), true, false);
            c.LineTo(new Point(x + 23, y + 2), true, false);
        }
        dc.DrawGeometry(null, pen, hose);
        dc.DrawLine(pen, new Point(x + 26, y + 6), new Point(x + 26, y + 11));
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth / 340, ActualHeight / 340));
        for (int i = 8; i > 0; i--) dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(11 - i), 0, 0, 0)), null, new Point(170, 175), 157 + i, 157 + i);
        dc.DrawEllipse(new LinearGradientBrush(Color.FromRgb(63, 79, 94), Color.FromRgb(17, 26, 35), 90), new Pen(Brush("#556776"), .8), new Point(170, 170), 155, 155);
        var face = new RadialGradientBrush { Center = new Point(.35, .15), GradientOrigin = new Point(.35, .15), RadiusX = .9, RadiusY = .9 };
        face.GradientStops.Add(new GradientStop(Color.FromRgb(32, 46, 60), 0)); face.GradientStops.Add(new GradientStop(Color.FromRgb(10, 17, 25), 1));
        dc.DrawEllipse(face, new Pen(Brush("#080F17"), 2), new Point(170, 170), 150, 150);
        dc.DrawEllipse(null, new Pen(Brush("#344351"), .6), new Point(170, 170), 145, 145);
        dc.PushOpacity(.35);
        Arc(dc, 0, 9.5, 127, Red, 8);
        Arc(dc, 10.5, 19.5, 127, Yellow, 8);
        Arc(dc, 20.5, 100, 127, Green, 8);
        dc.Pop();
        var accent = Quota == null || !Fresh ? Muted : FuelColor(Quota.Remaining);
        if (Quota != null)
        {
            dc.PushOpacity(Fresh ? 1 : .35);
            Arc(dc, 0, Math.Min(Value, 9.5), 127, Red, 8);
            Arc(dc, 10.5, Math.Min(Value, 19.5), 127, Yellow, 8);
            Arc(dc, 20.5, Value, 127, Green, 8);
            dc.Pop();
        }
        for (int i = 0; i <= 100; i += 5)
        {
            bool major = i % 25 == 0;
            dc.DrawLine(new Pen(major ? Brush("#B1C0CB") : Brush("#536574"), major ? 1.7 : .8), At(i, 116), At(i, major ? 106 : 111));
            if (major)
            {
                var p = At(i, 93);
                string label = i switch { 0 => "0", 25 => "¼", 50 => "½", 75 => "¾", _ => "1" };
                Text(dc, label, p.X, p.Y - 10, 18, i == 0 ? Red : i == 100 ? Green : Muted, true);
            }
        }
        Text(dc, "C O D E X", 170, 96, 14, White, true);
        FuelPump(dc, 158, 120, accent);
        if (Quota != null)
        {
            dc.PushOpacity(Fresh ? 1 : .45);
            var tip = At(Value, 83); var tail = At(Value, -15); var a = (140 + Value * 2.6) * Math.PI / 180;
            var needle = new StreamGeometry();
            using (var c = needle.Open())
            {
                c.BeginFigure(tip, true, true);
                c.LineTo(new Point(tail.X - Math.Sin(a) * 3.5, tail.Y + Math.Cos(a) * 3.5), true, false);
                c.LineTo(new Point(tail.X + Math.Sin(a) * 3.5, tail.Y - Math.Cos(a) * 3.5), true, false);
            }
            dc.DrawGeometry(accent, null, needle);
            dc.Pop();
        }
        dc.DrawEllipse(Brush("#344858"), new Pen(Brush("#677D8D"), 1), new Point(170, 168), 9, 9);
        dc.DrawEllipse(Brush("#0F1B27"), null, new Point(170, 168), 4, 4);
        var percentage = Quota is null ? "—" : $"{Quota.Remaining:0.#}%";
        Text(dc, percentage, 170, 201, percentage.Length > 4 ? 36 : 40, White, true);
        var lowFuel = Quota != null && Fresh && Quota.Remaining < 10;
        Text(dc, lowFuel ? "ТОПЛИВО НА ИСХОДЕ" : "ЗАПАС · " + (Quota?.Period.ToUpperInvariant() ?? "ЛИМИТ"), 170, 257, 11, lowFuel ? Red : Muted, lowFuel);
        string reset = Quota?.Reset is { } whenReset ? "Сброс " + whenReset.ToLocalTime().ToString("d MMM · HH:mm", CultureInfo.GetCultureInfo("ru-RU")) : "Сброс неизвестен";
        Text(dc, Quota == null ? "Правый клик · меню" : reset, 170, 276, 11, Muted);
        Text(dc, Preview ? "ДЕМО · 10 СЕК" : Status, 170, 295, 10, Fresh ? Muted : Brush("#E3B775"));
        dc.Pop();
    }
}
