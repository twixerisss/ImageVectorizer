using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Svg.Skia;
using SkiaSharp;

namespace ImageVectorizer;

public class VectorizationEngine
{
    public VectorizationResult Vectorize(string imagePath, VectorizationSettings settings)
    {
        using var image = Image.Load<Rgba32>(imagePath);
        
        var width = image.Width;
        var height = image.Height;

        // Step 1: Quantize colors
        var quantizedColors = QuantizeColors(image, settings.ColorCount);
        
        // Step 2: Create color regions
        var regions = CreateColorRegions(image, quantizedColors, settings);
        
        // Step 3: Trace contours for each region
        var paths = new List<SvgPath>();
        foreach (var region in regions)
        {
            var contours = TraceContours(region, settings);
            paths.AddRange(contours);
        }

        // Step 4: Generate SVG
        var svgContent = GenerateSvg(width, height, paths, settings);

        // Step 5: Create PNG preview
        var pngBytes = RenderSvgToPng(svgContent, width, height);

        return new VectorizationResult
        {
            SvgContent = svgContent,
            PngBytes = pngBytes,
            PathCount = paths.Count,
            ColorCount = quantizedColors.Count
        };
    }

    private List<Rgba32> QuantizeColors(Image<Rgba32> image, int targetColors)
    {
        var pixels = new List<Rgba32>();
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A > 10)
                    {
                        pixels.Add(row[x]);
                    }
                }
            }
        });

        if (pixels.Count == 0)
            return new List<Rgba32> { new Rgba32(0, 0, 0, 0) };

        return MedianCutQuantize(pixels, targetColors);
    }

    private List<Rgba32> MedianCutQuantize(List<Rgba32> pixels, int targetColors)
    {
        if (pixels.Count <= targetColors)
            return pixels.Distinct().ToList();

        var buckets = new List<List<Rgba32>> { pixels };

        while (buckets.Count < targetColors)
        {
            var largestBucket = buckets.OrderByDescending(b => GetColorRange(b)).First();
            buckets.Remove(largestBucket);

            if (largestBucket.Count <= 1)
            {
                buckets.Add(largestBucket);
                continue;
            }

            var (bucket1, bucket2) = SplitBucket(largestBucket);
            buckets.Add(bucket1);
            buckets.Add(bucket2);
        }

        return buckets.Select(GetAverageColor).ToList();
    }

    private double GetColorRange(List<Rgba32> pixels)
    {
        if (pixels.Count == 0) return 0;

        var rRange = pixels.Max(p => p.R) - pixels.Min(p => p.R);
        var gRange = pixels.Max(p => p.G) - pixels.Min(p => p.G);
        var bRange = pixels.Max(p => p.B) - pixels.Min(p => p.B);

        return Math.Max(rRange, Math.Max(gRange, bRange));
    }

    private (List<Rgba32>, List<Rgba32>) SplitBucket(List<Rgba32> pixels)
    {
        var rRange = pixels.Max(p => p.R) - pixels.Min(p => p.R);
        var gRange = pixels.Max(p => p.G) - pixels.Min(p => p.G);
        var bRange = pixels.Max(p => p.B) - pixels.Min(p => p.B);

        List<Rgba32> sorted;
        if (rRange >= gRange && rRange >= bRange)
            sorted = pixels.OrderBy(p => p.R).ToList();
        else if (gRange >= rRange && gRange >= bRange)
            sorted = pixels.OrderBy(p => p.G).ToList();
        else
            sorted = pixels.OrderBy(p => p.B).ToList();

        var mid = sorted.Count / 2;
        return (sorted.Take(mid).ToList(), sorted.Skip(mid).ToList());
    }

    private Rgba32 GetAverageColor(List<Rgba32> pixels)
    {
        if (pixels.Count == 0) return new Rgba32(0, 0, 0, 0);

        var r = (byte)pixels.Average(p => p.R);
        var g = (byte)pixels.Average(p => p.G);
        var b = (byte)pixels.Average(p => p.B);
        var a = (byte)pixels.Average(p => p.A);

        return new Rgba32(r, g, b, a);
    }

    private Rgba32 FindClosestColor(Rgba32 pixel, List<Rgba32> palette)
    {
        return palette.OrderBy(c => ColorDistance(pixel, c)).First();
    }

    private double ColorDistance(Rgba32 c1, Rgba32 c2)
    {
        var dr = c1.R - c2.R;
        var dg = c1.G - c2.G;
        var db = c1.B - c2.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private List<ColorRegion> CreateColorRegions(Image<Rgba32> image, List<Rgba32> palette, VectorizationSettings settings)
    {
        var width = image.Width;
        var height = image.Height;
        var colorMap = new Rgba32[width, height];

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    if (settings.PreserveTransparency && pixel.A < 128)
                    {
                        colorMap[x, y] = new Rgba32(0, 0, 0, 0);
                    }
                    else
                    {
                        colorMap[x, y] = FindClosestColor(pixel, palette);
                    }
                }
            }
        });

        var regions = new Dictionary<Rgba32, ColorRegion>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var color = colorMap[x, y];
                if (color.A == 0) continue;

                if (!regions.ContainsKey(color))
                {
                    regions[color] = new ColorRegion
                    {
                        Color = color,
                        Pixels = new bool[width, height],
                        Width = width,
                        Height = height
                    };
                }
                regions[color].Pixels[x, y] = true;
            }
        }

        return regions.Values.ToList();
    }

    private List<SvgPath> TraceContours(ColorRegion region, VectorizationSettings settings)
    {
        var paths = new List<SvgPath>();
        var visited = new bool[region.Width, region.Height];

        for (int y = 0; y < region.Height - 1; y++)
        {
            for (int x = 0; x < region.Width - 1; x++)
            {
                if (region.Pixels[x, y] && !visited[x, y])
                {
                    var contour = TraceSingleContour(region, x, y, visited, settings);
                    if (contour.Points.Count >= 3)
                    {
                        if (settings.SimplifyPaths)
                        {
                            contour.Points = SimplifyPath(contour.Points, settings.DetailLevel);
                        }
                        if (settings.Smoothing > 0)
                        {
                            contour.Points = SmoothPath(contour.Points, settings.Smoothing);
                        }
                        paths.Add(contour);
                    }
                }
            }
        }

        return paths;
    }

    private SvgPath TraceSingleContour(ColorRegion region, int startX, int startY, bool[,] visited, VectorizationSettings settings)
    {
        var points = new List<(double X, double Y)>();
        var color = region.Color;

        var directions = new (int dx, int dy)[] { (1, 0), (0, 1), (-1, 0), (0, -1) };
        var x = startX;
        var y = startY;
        var dir = 0;
        var step = Math.Max(1, 11 - settings.DetailLevel);

        var iterations = 0;
        var maxIterations = region.Width * region.Height;

        do
        {
            if (iterations++ > maxIterations) break;

            points.Add((x, y));
            visited[x, y] = true;

            var found = false;
            for (int i = 0; i < 4; i++)
            {
                var newDir = (dir + 3 + i) % 4;
                var nx = x + directions[newDir].dx * step;
                var ny = y + directions[newDir].dy * step;

                if (nx >= 0 && nx < region.Width && ny >= 0 && ny < region.Height && region.Pixels[nx, ny])
                {
                    x = nx;
                    y = ny;
                    dir = newDir;
                    found = true;
                    break;
                }
            }

            if (!found) break;
        }
        while (x != startX || y != startY);

        return new SvgPath { Color = color, Points = points };
    }

    private List<(double X, double Y)> SimplifyPath(List<(double X, double Y)> points, int detailLevel)
    {
        if (points.Count < 3) return points;

        var epsilon = Math.Max(0.5, (11 - detailLevel) * 0.5);
        return DouglasPeucker(points, epsilon);
    }

    private List<(double X, double Y)> DouglasPeucker(List<(double X, double Y)> points, double epsilon)
    {
        if (points.Count < 3) return points;

        var dmax = 0.0;
        var index = 0;
        var end = points.Count - 1;

        for (int i = 1; i < end; i++)
        {
            var d = PerpendicularDistance(points[i], points[0], points[end]);
            if (d > dmax)
            {
                index = i;
                dmax = d;
            }
        }

        if (dmax > epsilon)
        {
            var firstPart = DouglasPeucker(points.Take(index + 1).ToList(), epsilon);
            var secondPart = DouglasPeucker(points.Skip(index).ToList(), epsilon);

            return firstPart.Take(firstPart.Count - 1).Concat(secondPart).ToList();
        }
        else
        {
            return new List<(double X, double Y)> { points[0], points[end] };
        }
    }

    private double PerpendicularDistance((double X, double Y) point, (double X, double Y) lineStart, (double X, double Y) lineEnd)
    {
        var dx = lineEnd.X - lineStart.X;
        var dy = lineEnd.Y - lineStart.Y;

        if (dx == 0 && dy == 0)
        {
            return Math.Sqrt(Math.Pow(point.X - lineStart.X, 2) + Math.Pow(point.Y - lineStart.Y, 2));
        }

        var t = ((point.X - lineStart.X) * dx + (point.Y - lineStart.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Max(0, Math.Min(1, t));

        var nearestX = lineStart.X + t * dx;
        var nearestY = lineStart.Y + t * dy;

        return Math.Sqrt(Math.Pow(point.X - nearestX, 2) + Math.Pow(point.Y - nearestY, 2));
    }

    private List<(double X, double Y)> SmoothPath(List<(double X, double Y)> points, int smoothing)
    {
        if (points.Count < 3 || smoothing == 0) return points;

        var result = new List<(double X, double Y)>();
        var factor = smoothing / 20.0;

        for (int i = 0; i < points.Count; i++)
        {
            var prev = points[(i - 1 + points.Count) % points.Count];
            var curr = points[i];
            var next = points[(i + 1) % points.Count];

            var smoothX = curr.X + factor * ((prev.X + next.X) / 2 - curr.X);
            var smoothY = curr.Y + factor * ((prev.Y + next.Y) / 2 - curr.Y);

            result.Add((smoothX, smoothY));
        }

        return result;
    }

    private string GenerateSvg(int width, int height, List<SvgPath> paths, VectorizationSettings settings)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width} {height}\" width=\"{width}\" height=\"{height}\">");

        if (settings.AntiAliasing)
        {
            sb.AppendLine("  <defs><style>path { shape-rendering: geometricPrecision; }</style></defs>");
        }

        foreach (var path in paths.Where(p => p.Points.Count >= 2))
        {
            var color = $"#{path.Color.R:X2}{path.Color.G:X2}{path.Color.B:X2}";
            var opacity = path.Color.A / 255.0;

            var pathData = new StringBuilder();
            pathData.Append($"M {path.Points[0].X:F1} {path.Points[0].Y:F1}");

            for (int i = 1; i < path.Points.Count; i++)
            {
                pathData.Append($" L {path.Points[i].X:F1} {path.Points[i].Y:F1}");
            }
            pathData.Append(" Z");

            sb.AppendLine($"  <path d=\"{pathData}\" fill=\"{color}\" fill-opacity=\"{opacity:F2}\"/>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private byte[]? RenderSvgToPng(string svgContent, int width, int height)
    {
        try
        {
            using var svg = new SKSvg();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svgContent));
            svg.Load(stream);
            
            if (svg.Picture == null) return null;

            var info = new SKImageInfo(width, height);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.DrawPicture(svg.Picture);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            
            return data.ToArray();
        }
        catch
        {
            return null;
        }
    }
}

public class ColorRegion
{
    public Rgba32 Color { get; set; }
    public bool[,] Pixels { get; set; } = new bool[0, 0];
    public int Width { get; set; }
    public int Height { get; set; }
}

public class SvgPath
{
    public Rgba32 Color { get; set; }
    public List<(double X, double Y)> Points { get; set; } = new();
}
