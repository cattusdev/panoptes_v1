using Microsoft.Extensions.Options;
using NetVips;
using Vips = NetVips.NetVips;
using VipsImage = NetVips.Image;

namespace Panoptes.Services.Media
{
    public record ProcessedVariant(string Name, int Width, int Height, byte[] Data, string ContentType, string Extension);

    public record ProcessedImage(int Width, int Height, string? DominantColor, IReadOnlyList<ProcessedVariant> Variants);

    // Ο server είναι πολύ απασχολημένος (πολλές εικόνες ταυτόχρονα): ο χρήστης ξαναδοκιμάζει σε λίγο.
    // Το Message είναι και το key της μετάφρασης.
    public class MediaBusyException : Exception
    {
        public MediaBusyException() : base("The server is busy processing other images. Please try again in a moment.") { }
    }

    // Επεξεργασία εικόνων με libvips (NetVips): γρήγορη και με λίγη μνήμη (shrink-on-load, streaming).
    // Κάθε εικόνα ξαναγράφεται από την αρχή: σωστός προσανατολισμός, sRGB, χωρίς metadata (EXIF/GPS).
    public sealed class ImageProcessor
    {
        public const string Thumb = "thumb", Medium = "medium", Large = "large", Original = "original";

        private readonly MediaOptions _options;
        private readonly SemaphoreSlim _gate;

        public ImageProcessor(IOptions<MediaOptions> options, ILogger<ImageProcessor> logger)
        {
            if (!ModuleInitializer.VipsInitialized)
                throw new InvalidOperationException("libvips could not be loaded.", ModuleInitializer.Exception);

            // Χωρίς cache (δεν κρατά ανοιχτά αρχεία ούτε μνήμη ανάμεσα στα uploads)· μόνο οι αξιόπιστοι loaders
            Cache.Max = 0;
            Cache.MaxFiles = 0;
            Cache.MaxMem = 0;
            Vips.Concurrency = 2;
            Vips.BlockUntrusted = true;

            _options = options.Value;
            var slots = _options.MaxConcurrentProcessing > 0 ? _options.MaxConcurrentProcessing : Math.Max(1, Environment.ProcessorCount / 2);
            _gate = new SemaphoreSlim(slots, slots);

            logger.LogInformation("libvips {Major}.{Minor}.{Patch} ready ({Slots} parallel images)",
                Vips.Version(0), Vips.Version(1), Vips.Version(2), slots);
        }

        public async Task<ProcessedImage> ProcessAsync(string path, MediaFormat format, CancellationToken cancellationToken = default)
        {
            if (!await _gate.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
                throw new MediaBusyException();
            try
            {
                return await Task.Run(() => Process(path, format), cancellationToken);
            }
            catch (VipsException)
            {
                throw new MediaValidationException("The image couldn't be read. It may be damaged.");
            }
            finally
            {
                _gate.Release();
            }
        }

        private ProcessedImage Process(string path, MediaFormat format)
        {
            var (width, height, pages) = Probe(path, format);
            var animated = pages > 1;
            var source = animated ? path + "[n=-1]" : path; // Όλα τα frames για τα κινούμενα GIF/WebP

            var variants = new List<ProcessedVariant>();

            // Original: πλήρης ανάλυση, ίδια μορφή, καθαρό. Το "κουτί" είναι τετράγωνο ώστε η περιστροφή (EXIF) να μη μικρύνει την εικόνα.
            var box = Math.Max(width, height);
            int originalWidth, originalHeight;
            using (var original = Resize(source, box, box))
            {
                originalWidth = original.Width;
                originalHeight = original.PageHeight;
                variants.Add(Encode(Original, original, format));
            }

            string? dominantColor;
            using (var thumb = VipsImage.Thumbnail(path, _options.ThumbSize, height: _options.ThumbSize,
                       size: Enums.Size.Down, crop: Enums.Interesting.Attention, outputProfile: "srgb", failOn: Enums.FailOn.Error))
            {
                variants.Add(Webp(Thumb, thumb));
                dominantColor = DominantColor(thumb);
            }

            using (var medium = Resize(source, _options.MediumWidth, 10_000_000))
                variants.Add(Webp(Medium, medium));
            using (var large = Resize(source, _options.LargeWidth, 10_000_000))
                variants.Add(Webp(Large, large));

            return new ProcessedImage(originalWidth, originalHeight, dominantColor, variants);
        }

        // Μόνο η κεφαλίδα (χωρίς αποκωδικοποίηση): όρια pixels/frames και ότι ο loader ταιριάζει με τον τύπο
        private (int Width, int Height, int Pages) Probe(string path, MediaFormat format)
        {
            using var probe = VipsImage.NewFromFile(path, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error);

            var loader = probe.Get("vips-loader") as string ?? "";
            var expected = format switch
            {
                MediaFormat.Jpeg => "jpegload",
                MediaFormat.Png => "pngload",
                MediaFormat.Gif => "gifload",
                MediaFormat.Webp => "webpload",
                _ => "-",
            };
            if (!loader.StartsWith(expected, StringComparison.Ordinal))
                throw new MediaValidationException("The image couldn't be read. It may be damaged.");

            var pages = probe.Contains("n-pages") ? Convert.ToInt32(probe.Get("n-pages")) : 1;
            var animated = pages > 1 && format is MediaFormat.Gif or MediaFormat.Webp;
            if (!animated) pages = 1;

            if (pages > _options.MaxFrames)
                throw new MediaValidationException("The animation has too many frames ({0:N0}). The limit is {1:N0}.", pages, _options.MaxFrames);

            if ((long)probe.Width * probe.Height * pages > _options.MaxPixels)
            {
                var megapixels = _options.MaxPixels / 1_000_000.0;
                throw pages > 1
                    ? new MediaValidationException("The image is too large ({0:N0} × {1:N0}, {2} frames). The limit is {3:0.#} megapixels.", probe.Width, probe.Height, pages, megapixels)
                    : new MediaValidationException("The image is too large ({0:N0} × {1:N0}). The limit is {2:0.#} megapixels.", probe.Width, probe.Height, megapixels);
            }

            return (probe.Width, probe.Height, pages);
        }

        // Thumbnail = shrink-on-load + αυτόματη περιστροφή + μετατροπή σε sRGB, ποτέ μεγέθυνση
        private static VipsImage Resize(string source, int width, int height) =>
            VipsImage.Thumbnail(source, width, height: height, size: Enums.Size.Down, outputProfile: "srgb", failOn: Enums.FailOn.Error);

        private static ProcessedVariant Encode(string name, VipsImage image, MediaFormat format) => format switch
        {
            MediaFormat.Jpeg => Variant(name, image, image.JpegsaveBuffer(q: 90, optimizeCoding: true, interlace: true, keep: Enums.ForeignKeep.None), "image/jpeg", "jpg"),
            MediaFormat.Png => Variant(name, image, image.PngsaveBuffer(compression: 6, keep: Enums.ForeignKeep.None), "image/png", "png"),
            MediaFormat.Gif => Variant(name, image, image.GifsaveBuffer(keep: Enums.ForeignKeep.None), "image/gif", "gif"),
            _ => Variant(name, image, image.WebpsaveBuffer(q: 90, keep: Enums.ForeignKeep.None), "image/webp", "webp"),
        };

        private static ProcessedVariant Webp(string name, VipsImage image) =>
            Variant(name, image, image.WebpsaveBuffer(q: 80, effort: 4, keep: Enums.ForeignKeep.None), "image/webp", "webp");

        private static ProcessedVariant Variant(string name, VipsImage image, byte[] data, string contentType, string extension) =>
            new(name, image.Width, image.PageHeight, data, contentType, extension);

        // Μέσο χρώμα (για placeholder όσο φορτώνει η εικόνα)
        private static string? DominantColor(VipsImage thumb)
        {
            using var pixel = thumb.ThumbnailImage(1, height: 1, size: Enums.Size.Force);
            var values = pixel.Getpoint(0, 0);
            if (values.Length == 0) return null;
            int Channel(int i) => (int)Math.Clamp(Math.Round(values[Math.Min(i, values.Length < 3 ? 0 : i)]), 0, 255);
            return $"#{Channel(0):x2}{Channel(1):x2}{Channel(2):x2}";
        }
    }
}
