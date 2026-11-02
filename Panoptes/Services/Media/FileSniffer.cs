using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Localization;
using Panoptes.Models.Media;

namespace Panoptes.Services.Media
{
    // Λάθος που βλέπει ο χρήστης (π.χ. "This file type isn't allowed.").
    // Text = το αγγλικό κείμενο με {0}, {1}… (= το key της μετάφρασης), Args = οι τιμές.
    // Μεταφράζεται εκεί που φτάνει στον χρήστη (Localize)· το Message μένει αγγλικό (logs).
    public class MediaValidationException : Exception
    {
        public MediaValidationException(string text, params object[] args) : base(string.Format(CultureInfo.InvariantCulture, text, args))
        {
            Text = text;
            Args = args;
        }

        public string Text { get; }
        public object[] Args { get; }

        public string Localize(IStringLocalizer localizer) => localizer[Text, Args];
    }

    public enum MediaFormat { Jpeg, Png, Gif, Webp, Svg, Pdf, Docx, Xlsx, Pptx, Odt, Ods, Odp, Txt, Csv, Zip }

    public record DetectedFile(MediaKind Kind, MediaFormat Format, string Extension, string ContentType)
    {
        public bool IsRasterImage => Kind == MediaKind.Image && Format != MediaFormat.Svg;
    }

    // Ο πραγματικός τύπος ενός αρχείου από το περιεχόμενό του (magic bytes, δομή zip κ.λπ.).
    // Η κατάληξη πρέπει να συμφωνεί με το περιεχόμενο: ένα .exe με όνομα .jpg απορρίπτεται.
    public static class FileSniffer
    {
        private static readonly Dictionary<string, MediaFormat> Formats = new()
        {
            ["jpg"] = MediaFormat.Jpeg, ["jpeg"] = MediaFormat.Jpeg, ["png"] = MediaFormat.Png, ["gif"] = MediaFormat.Gif,
            ["webp"] = MediaFormat.Webp, ["svg"] = MediaFormat.Svg, ["pdf"] = MediaFormat.Pdf, ["docx"] = MediaFormat.Docx,
            ["xlsx"] = MediaFormat.Xlsx, ["pptx"] = MediaFormat.Pptx, ["odt"] = MediaFormat.Odt, ["ods"] = MediaFormat.Ods,
            ["odp"] = MediaFormat.Odp, ["txt"] = MediaFormat.Txt, ["csv"] = MediaFormat.Csv, ["zip"] = MediaFormat.Zip,
        };

        public static readonly IReadOnlyDictionary<MediaFormat, string> ContentTypes = new Dictionary<MediaFormat, string>
        {
            [MediaFormat.Jpeg] = "image/jpeg",
            [MediaFormat.Png] = "image/png",
            [MediaFormat.Gif] = "image/gif",
            [MediaFormat.Webp] = "image/webp",
            [MediaFormat.Svg] = "image/svg+xml",
            [MediaFormat.Pdf] = "application/pdf",
            [MediaFormat.Docx] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [MediaFormat.Xlsx] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [MediaFormat.Pptx] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [MediaFormat.Odt] = "application/vnd.oasis.opendocument.text",
            [MediaFormat.Ods] = "application/vnd.oasis.opendocument.spreadsheet",
            [MediaFormat.Odp] = "application/vnd.oasis.opendocument.presentation",
            [MediaFormat.Txt] = "text/plain",
            [MediaFormat.Csv] = "text/csv",
            [MediaFormat.Zip] = "application/zip",
        };

        public static string ExtensionOf(string fileName) => Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

        // Έλεγχος μόνο με το όνομα (πριν ανέβει το αρχείο): επιστρέφει το είδος ή πετάει μήνυμα για τον χρήστη
        public static MediaKind CheckName(string fileName, MediaOptions options)
        {
            var extension = ExtensionOf(fileName);
            var kind = options.KindForExtension(extension);
            if (kind != null) return kind.Value;

            throw new MediaValidationException(extension switch
            {
                "heic" or "heif" => "HEIC photos aren't supported. Export the photo as JPEG and try again.",
                "docm" or "xlsm" or "pptm" or "dotm" or "xltm" or "potm" => "Office files with macros aren't allowed.",
                "doc" or "xls" or "ppt" => "Old Office formats (.doc, .xls, .ppt) aren't allowed. Save the file as .docx, .xlsx or .pptx.",
                "svg" => "SVG files aren't allowed.",
                "" => "Files without an extension aren't allowed.",
                _ => "This file type (.{0}) isn't allowed.",
            }, extension);
        }

        public static DetectedFile Inspect(string path, string fileName, MediaOptions options)
        {
            var kind = CheckName(fileName, options);
            var extension = ExtensionOf(fileName);
            var format = Formats[extension];

            var header = new byte[64];
            int read;
            using (var stream = File.OpenRead(path))
                read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            var head = header.AsSpan(0, read);

            var matches = format switch
            {
                MediaFormat.Jpeg => head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
                MediaFormat.Png => head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
                MediaFormat.Gif => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8),
                MediaFormat.Webp => read >= 12 && head.StartsWith("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WEBP"u8),
                MediaFormat.Pdf => head.StartsWith("%PDF-"u8),
                MediaFormat.Docx or MediaFormat.Xlsx or MediaFormat.Pptx or MediaFormat.Odt or MediaFormat.Ods or MediaFormat.Odp
                    => head.StartsWith("PK\x03\x04"u8),
                MediaFormat.Zip => head.StartsWith("PK\x03\x04"u8) || head.StartsWith("PK\x05\x06"u8),
                MediaFormat.Txt or MediaFormat.Csv => IsUtf8Text(path),
                MediaFormat.Svg => true, // Ελέγχεται από τον SvgSanitizer (πρέπει να είναι έγκυρο SVG XML)
                _ => false,
            };

            if (!matches)
                throw new MediaValidationException("The file's content doesn't match its .{0} extension.", extension);

            // Zip-based μορφές: όρια (zip bomb) και σωστή εσωτερική δομή
            if (format is MediaFormat.Zip or MediaFormat.Docx or MediaFormat.Xlsx or MediaFormat.Pptx or MediaFormat.Odt or MediaFormat.Ods or MediaFormat.Odp)
            {
                using var archive = ZipInspector.Open(path, options);
                if (format != MediaFormat.Zip && !HasOfficeStructure(archive, format))
                    throw new MediaValidationException("The file's content doesn't match its .{0} extension.", extension);
            }

            return new DetectedFile(kind, format, extension == "jpeg" ? "jpg" : extension, ContentTypes[format]);
        }

        private static bool HasOfficeStructure(ZipArchive archive, MediaFormat format)
        {
            if (format is MediaFormat.Odt or MediaFormat.Ods or MediaFormat.Odp)
            {
                // ODF: το πρώτο entry "mimetype" περιέχει τον τύπο του εγγράφου
                var mimetype = archive.GetEntry("mimetype");
                if (mimetype == null || mimetype.Length > 100) return false;
                using var reader = new StreamReader(mimetype.Open());
                return reader.ReadToEnd().Trim() == ContentTypes[format];
            }

            // OOXML: [Content_Types].xml + ο φάκελος του προγράμματος. Χωρίς macros.
            if (archive.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
                throw new MediaValidationException("Office files with macros aren't allowed.");

            var folder = format switch { MediaFormat.Docx => "word/", MediaFormat.Xlsx => "xl/", _ => "ppt/" };
            return archive.GetEntry("[Content_Types].xml") != null &&
                   archive.Entries.Any(e => e.FullName.StartsWith(folder, StringComparison.Ordinal));
        }

        // Κείμενο: έγκυρο UTF-8, χωρίς NUL bytes (αλλιώς είναι binary με αλλαγμένο όνομα)
        private static bool IsUtf8Text(string path)
        {
            var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetDecoder();
            var bytes = new byte[81920];
            var chars = new char[81920];
            using var stream = File.OpenRead(path);
            try
            {
                int read;
                while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
                {
                    if (bytes.AsSpan(0, read).Contains((byte)0)) return false;
                    decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                }
                decoder.GetChars(bytes, 0, 0, chars, 0, flush: true);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }
    }

    // Διαβάζει μόνο τον κατάλογο του zip (ποτέ δεν αποσυμπιέζει στον server)
    public static class ZipInspector
    {
        public static ZipArchive Open(string path, MediaOptions options)
        {
            ZipArchive archive;
            try
            {
                archive = ZipFile.OpenRead(path);
            }
            catch (InvalidDataException)
            {
                throw new MediaValidationException("The archive is damaged or isn't a valid zip file.");
            }

            try
            {
                Check(archive, options);
                return archive;
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }

        private static void Check(ZipArchive archive, MediaOptions options)
        {
            if (archive.Entries.Count > options.MaxZipEntries)
                throw new MediaValidationException("The archive has too many files (more than {0:N0}).", options.MaxZipEntries);

            long total = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.IsEncrypted)
                    throw new MediaValidationException("Password-protected archives aren't allowed.");

                var name = entry.FullName.Replace('\\', '/');
                if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Contains(".."))
                    throw new MediaValidationException("The archive contains unsafe file paths.");

                total += entry.Length;
                var suspiciousRatio = entry.Length > 1024 * 1024 &&
                    (entry.CompressedLength == 0 || entry.Length / entry.CompressedLength > options.MaxZipCompressionRatio);
                if (total > options.MaxZipUncompressedBytes || suspiciousRatio)
                    throw new MediaValidationException("The archive expands to an unsafe size.");
            }
        }
    }
}
