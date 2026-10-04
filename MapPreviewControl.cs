namespace Ra2ModeLauncher;

internal sealed class MapPreviewControl : Control
{
    private MapInfo? map;
    private Bitmap? preview;
    private readonly Dictionary<int, (Color Color, string Name)> occupants = [];
    private static readonly Color[] PlayerColors = [Color.Gold, Color.Red, Color.DeepSkyBlue, Color.LimeGreen, Color.Orange, Color.Cyan, Color.MediumPurple, Color.HotPink];
    public bool AllowStartSelection { get; set; }
    public void SetPlayers(IEnumerable<(int Start, int Color, string Name)> players)
    {
        occupants.Clear();
        foreach (var player in players) occupants[player.Start] = (PlayerColors[Math.Clamp(player.Color, 0, 7)], player.Name);
        Invalidate();
    }
    public event Action<int>? StartSelected;

    public MapInfo? Map
    {
        get => map;
        set
        {
            if (ReferenceEquals(map, value)) return;
            preview?.Dispose();
            map = value;
            preview = value is null ? null : MapPreviewExtractor.Extract(value.Path);
            Invalidate();
        }
    }

    public MapPreviewControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(30, 42, 32);
        Cursor = Cursors.Hand;
        MinimumSize = new Size(180, 120);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Rectangle area = PreviewArea();
        string caption = map is null ? "没有可用地图：请选择房间；缺少地图或版本不一致时暂不预览" : $"{map.Name} · {map.StartingPoints} 人 · {(AllowStartSelection ? "点击未占用编号设置自己的出生点" : "地图预览（只读）")}";
        TextRenderer.DrawText(e.Graphics, caption, Font, new Rectangle(12, 4, Math.Max(1, Width - 24), 30), Color.White, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        if (preview is not null)
        {
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(preview, area);
        }
        else
        {
            using var fill = new System.Drawing.Drawing2D.LinearGradientBrush(area, Color.FromArgb(88, 116, 66), Color.FromArgb(45, 75, 54), 45f);
            e.Graphics.FillRectangle(fill, area);
            TextRenderer.DrawText(e.Graphics, map is null ? "暂无地图" : "地图未包含可读取的缩略图", Font, area, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        e.Graphics.DrawRectangle(Pens.DarkSeaGreen, area);

        if (map is null || map.StartPositions.Count == 0)
        {
            return;
        }
        for (int i = 0; i < map.StartPositions.Count; i++) DrawStart(e.Graphics, area, map.StartPositions[i], i + 1);
        int legendX = 14;
        foreach (var item in occupants.OrderBy(item => item.Key))
        {
            string label = $"{item.Key}: {item.Value.Name}";
            int labelWidth = Math.Min(140, TextRenderer.MeasureText(label, Font).Width + 12);
            TextRenderer.DrawText(e.Graphics, label, Font, new Rectangle(legendX, Height - 26, labelWidth, 22), item.Value.Color, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            legendX += labelWidth;
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (map is null || !AllowStartSelection || e.Button != MouseButtons.Left) return;
        Rectangle area = PreviewArea();
        int nearest = -1;
        double distance = 26;
        for (int i = 0; i < map.StartPositions.Count; i++)
        {
            Point point = ToPixel(area, map.StartPositions[i]);
            double candidate = Math.Sqrt(Math.Pow(e.X - point.X, 2) + Math.Pow(e.Y - point.Y, 2));
            if (candidate < distance) { distance = candidate; nearest = i; }
        }
        if (nearest >= 0) StartSelected?.Invoke(nearest + 1);
    }

    private Rectangle PreviewArea()
    {
        int padding = 14;
        var available = new Rectangle(padding, 38, Math.Max(1, ClientSize.Width - padding * 2 - 1), Math.Max(1, ClientSize.Height - 70));
        if (preview is null) return available;
        float imageRatio = preview.Width / (float)preview.Height;
        float availableRatio = available.Width / (float)available.Height;
        if (availableRatio > imageRatio)
        {
            int width = (int)(available.Height * imageRatio);
            return new Rectangle(available.Left + (available.Width - width) / 2, available.Top, width, available.Height);
        }
        int height = (int)(available.Width / imageRatio);
        return new Rectangle(available.Left, available.Top + (available.Height - height) / 2, available.Width, height);
    }

    private static Point ToPixel(Rectangle area, PointF position) => new(area.Left + (int)(position.X * area.Width), area.Top + (int)(position.Y * area.Height));

    private void DrawStart(Graphics graphics, Rectangle area, PointF position, int number)
    {
        Point point = ToPixel(area, position);
        const int radius = 14;
        Rectangle circle = new(point.X - radius, point.Y - radius, radius * 2, radius * 2);
        bool occupied = occupants.TryGetValue(number, out var owner);
        using var brush = new SolidBrush(occupied ? owner.Color : Color.FromArgb(225, 230, 235));
        using var pen = new Pen(Color.White, 2f);
        graphics.FillEllipse(brush, circle);
        graphics.DrawEllipse(pen, circle);
        using var boldFont = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(graphics, number.ToString(), boldFont, circle, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) preview?.Dispose();
        base.Dispose(disposing);
    }
}
