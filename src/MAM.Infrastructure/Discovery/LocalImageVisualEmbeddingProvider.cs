using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MAM.Application.Discovery;

namespace MAM.Infrastructure.Discovery;

public sealed class LocalImageVisualEmbeddingProvider : IVisualEmbeddingProvider
{
    private const int Grid = 16;
    private const int VectorDimensions = Grid * Grid * 3;
    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/bmp", "image/gif", "image/tiff", "image/webp"
    };

    public VisualProviderHealth Health => OperatingSystem.IsWindows()
        ? new VisualProviderHealth(true, "LocalImageGrid", "rgb-grid-16x16", 1, VectorDimensions, "Local deterministic visual descriptor is ready.")
        : new VisualProviderHealth(false, "LocalImageGrid", "rgb-grid-16x16", 1, VectorDimensions, "The local visual descriptor requires Windows image decoding.");

    public async Task<VisualEmbeddingDescriptor> EmbedAsync(Stream content, string? fileName, string? contentType, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new VisualSearchRequestException("visual_provider_unavailable", "Visual image decoding is unavailable on this operating system.", 503);
        if (content is null || !content.CanRead)
            throw new VisualSearchRequestException("image_required", "A readable image is required.");

        var normalizedType = NormalizeContentType(contentType);
        if (normalizedType is not null && !SupportedContentTypes.Contains(normalizedType))
            throw new VisualSearchRequestException("image_type_not_supported", "The image type is not supported for visual search.", 415);

        await using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > 16L * 1024 * 1024)
                throw new VisualSearchRequestException("image_too_large", "Visual search images are limited to 16 MB.", 413);
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (memory.Length == 0) throw new VisualSearchRequestException("image_empty", "The image is empty.");
        memory.Position = 0;

        try
        {
            using var source = Image.FromStream(memory, useEmbeddedColorManagement: true, validateImageData: true);
            if (source.Width < 2 || source.Height < 2)
                throw new VisualSearchRequestException("image_dimensions_invalid", "The image dimensions are too small for visual search.");
            if ((long)source.Width * source.Height > 80_000_000L)
                throw new VisualSearchRequestException("image_dimensions_too_large", "The decoded image dimensions exceed the visual search safety limit.", 413);

            using var canvas = new Bitmap(Grid, Grid, PixelFormat.Format24bppRgb);
            canvas.SetResolution(96, 96);
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.Black);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                var scale = Math.Min((double)Grid / source.Width, (double)Grid / source.Height);
                var width = Math.Max(1, (int)Math.Round(source.Width * scale));
                var height = Math.Max(1, (int)Math.Round(source.Height * scale));
                var x = (Grid - width) / 2;
                var y = (Grid - height) / 2;
                graphics.DrawImage(source, new Rectangle(x, y, width, height));
            }

            var values = new float[VectorDimensions];
            var position = 0;
            for (var y = 0; y < Grid; y++)
            {
                for (var x = 0; x < Grid; x++)
                {
                    var pixel = canvas.GetPixel(x, y);
                    values[position++] = (pixel.R / 255f) - 0.5f;
                    values[position++] = (pixel.G / 255f) - 0.5f;
                    values[position++] = (pixel.B / 255f) - 0.5f;
                }
            }
            Normalize(values);
            return new VisualEmbeddingDescriptor(Health.Provider, Health.ModelId, Health.ModelVersion, values.Length, values);
        }
        catch (VisualSearchRequestException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw new VisualSearchRequestException("image_decode_failed", "The supplied file is not a valid supported image.", 415);
        }
        catch (ExternalException)
        {
            throw new VisualSearchRequestException("image_decode_failed", "The supplied image could not be decoded safely.", 415);
        }
    }

    private static string? NormalizeContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var semicolon = value.IndexOf(';');
        return (semicolon >= 0 ? value[..semicolon] : value).Trim().ToLowerInvariant();
    }

    private static void Normalize(float[] values)
    {
        double sum = 0;
        for (var i = 0; i < values.Length; i++) sum += values[i] * values[i];
        var magnitude = Math.Sqrt(sum);
        if (magnitude < 1e-8) return;
        for (var i = 0; i < values.Length; i++) values[i] = (float)(values[i] / magnitude);
    }
}