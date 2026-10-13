using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CropQc.Web.Services;

/// <summary>Static PNG using the existing cross-platform image library; no browser, remote URL or system fonts.</summary>
internal static class QcSizeChart
{
    public static QcEmailInlineImage Render(QcSizeDistribution distribution, string key, string title)
    {
        const int left = 110, top = 55, bottom = 445, width = 1280;
        using var image = new Image<Rgb24>(width, 520, new Rgb24(255, 255, 255));
        var maximum = Math.Max(1, distribution.Counts.Max(x => x.Count));
        var step = Math.Max(1, (int)Math.Ceiling(maximum / 5d));
        var ceiling = (int)Math.Ceiling(maximum / (double)step) * step;
        for (var tick = 0; tick <= ceiling; tick += step)
        {
            var y = bottom - (int)((bottom - top) * (tick / (double)ceiling));
            Fill(left, y, width - left - 20, 2, new Rgb24(226, 232, 240));
            Digits(tick, left - 16, y - 14, alignRight: true);
        }
        var slot = (width - left - 20d) / distribution.Counts.Count;
        for (var i = 0; i < distribution.Counts.Count; i++)
        {
            var value = distribution.Counts[i];
            var center = left + (int)(slot * (i + .5));
            var height = (int)((bottom - top) * (value.Count / (double)ceiling));
            if (height > 0) Fill(center - (int)(slot * .3), bottom - height, (int)(slot * .6), height, new Rgb24(37, 99, 160));
            Digits(value.Count, center, bottom - height - 40);
            Digits(value.Size, center, bottom + 24);
        }
        Fill(left, bottom, width - left - 20, 2, new Rgb24(51, 65, 85));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return new($"cropqc-size-{key}@cropqc", $"size-{key}.png", "image/png", stream.ToArray(),
            title + "; box-count size: sampled fruit count; " + string.Join(", ", distribution.Counts.Select(x => $"{x.Size}: {x.Count}")));

        void Fill(int x, int y, int w, int h, Rgb24 color)
        {
            for (var row = Math.Max(0, y); row < Math.Min(image.Height, y + h); row++)
                for (var column = Math.Max(0, x); column < Math.Min(image.Width, x + w); column++)
                    image[column, row] = color;
        }

        void Digits(int value, int x, int y, bool alignRight = false)
        {
            // Small numeric bitmap alphabet keeps rendering deterministic on Linux and Windows.
            var text = value.ToString(CultureInfo.InvariantCulture);
            var scale = text.Length <= 3 ? 4 : 3;
            var start = x - (alignRight ? text.Length * 6 * scale : text.Length * 6 * scale / 2);
            foreach (var digit in text)
            {
                var glyph = Numerals[digit - '0'];
                for (var row = 0; row < 7; row++)
                    for (var column = 0; column < 5; column++)
                        if ((glyph[row] & (1 << (4 - column))) != 0)
                            Fill(start + column * scale, y + row * scale, scale, scale, new Rgb24(31, 41, 55));
                start += 6 * scale;
            }
        }
    }

    private static readonly int[][] Numerals =
    [
        [14,17,19,21,25,17,14], [4,12,4,4,4,4,14], [14,17,1,2,4,8,31],
        [30,1,1,14,1,1,30], [2,6,10,18,31,2,2], [31,16,16,30,1,1,30],
        [14,16,16,30,17,17,14], [31,1,2,4,8,8,8], [14,17,17,14,17,17,14], [14,17,17,15,1,1,14]
    ];
}
