using System.IO.Compression;
using System.Text;
using NetVips;
using VipsImage = NetVips.Image;

namespace Panoptes_v1.E2E;

// Αρχεία για τα tests του media, φτιαγμένα στη στιγμή (κανένα binary στο repo)
public static class MediaFiles
{
    // "Ιδιωτική" πληροφορία μέσα στα metadata της φωτογραφίας: δεν πρέπει να υπάρχει σε κανένα αρχείο που σερβίρουμε
    public const string SecretMarker = "GPS-SECRET-37.9838N-23.7275E";

    // Φωτογραφία 600x400 με EXIF orientation 6 (όρθια: 400x600) και XMP με το SecretMarker
    public static byte[] PhotoWithMetadata()
    {
        using var image = Gradient(600, 400);
        var xmp = $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>" +
                  $"<rdf:Description xmlns:dc='http://purl.org/dc/elements/1.1/' dc:source='{SecretMarker}'/></rdf:RDF></x:xmpmeta>";
        using var tagged = image.Mutate(m =>
        {
            m.Set(GValue.GIntType, "orientation", 6);
            m.Set(GValue.BlobType, "xmp-data", Encoding.UTF8.GetBytes(xmp));
        });
        return tagged.JpegsaveBuffer(q: 85, keep: Enums.ForeignKeep.All);
    }

    public static byte[] Png(int width = 120, int height = 80)
    {
        using var image = Gradient(width, height);
        return image.PngsaveBuffer();
    }

    public static byte[] Jpeg(int width = 120, int height = 80)
    {
        using var image = Gradient(width, height);
        return image.JpegsaveBuffer();
    }

    // Κινούμενο GIF με n frames
    public static byte[] AnimatedGif(int frames = 3, int width = 60, int height = 40)
    {
        var pages = Enumerable.Range(0, frames).Select(i => Gradient(width, height, shift: i * 40)).ToArray();
        try
        {
            using var strip = VipsImage.Arrayjoin(pages, across: 1);
            using var animated = strip.Mutate(m =>
            {
                m.Set(GValue.GIntType, "page-height", height);
                m.Set(GValue.ArrayIntType, "delay", Enumerable.Repeat(100, frames).ToArray());
                m.Set(GValue.GIntType, "loop", 0);
            });
            return animated.GifsaveBuffer();
        }
        finally
        {
            foreach (var page in pages) page.Dispose();
        }
    }

    // 8000x8000 = 64 MP (πάνω από το όριο των 50 MP), αλλά λίγα KB ως PNG
    public static byte[] HugePng()
    {
        using var image = VipsImage.Black(8000, 8000);
        return image.PngsaveBuffer(compression: 9);
    }

    public static (int Width, int Height, int Pages) Measure(byte[] data)
    {
        using var image = VipsImage.NewFromBuffer(data, access: Enums.Access.Sequential);
        var pages = image.Contains("n-pages") ? Convert.ToInt32(image.Get("n-pages")) : 1;
        return (image.Width, image.PageHeight, pages);
    }

    public static bool HasMetadata(byte[] data)
    {
        using var image = VipsImage.NewFromBuffer(data, access: Enums.Access.Sequential);
        return image.Contains("exif-data") || image.Contains("xmp-data") || image.Contains("iptc-data");
    }

    public static byte[] Pdf(int sizeBytes = 2048)
    {
        var header = "%PDF-1.4\n%âãÏÓ\n"u8.ToArray();
        var data = new byte[Math.Max(sizeBytes, header.Length)];
        header.CopyTo(data, 0);
        for (var i = header.Length; i < data.Length; i++) data[i] = (byte)('a' + i % 26);
        return data;
    }

    public static byte[] Docx(bool withMacros = false) => Zip(zip =>
    {
        Entry(zip, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
        Entry(zip, "word/document.xml", "<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>");
        if (withMacros) Entry(zip, "word/vbaProject.bin", "macro");
    });

    public static byte[] ZipWith(params (string Name, string Content)[] entries) => Zip(zip =>
    {
        foreach (var (name, content) in entries) Entry(zip, name, content);
    });

    // 50 MB μηδενικά που συμπιέζονται σε λίγα KB
    public static byte[] ZipBomb() => Zip(zip =>
    {
        var entry = zip.CreateEntry("zeros.bin", CompressionLevel.SmallestSize);
        using var stream = entry.Open();
        var block = new byte[1024 * 1024];
        for (var i = 0; i < 50; i++) stream.Write(block);
    });

    // Zip με τη σημαία "κρυπτογραφημένο" (bit 0) στο local και στο central header
    public static byte[] EncryptedZip()
    {
        var data = ZipWith(("secret.txt", "hello"));
        for (var i = 0; i + 4 < data.Length; i++)
        {
            if (data[i] == 0x50 && data[i + 1] == 0x4B && data[i + 2] == 0x03 && data[i + 3] == 0x04) data[i + 6] |= 1;
            if (data[i] == 0x50 && data[i + 1] == 0x4B && data[i + 2] == 0x01 && data[i + 3] == 0x02) data[i + 8] |= 1;
        }
        return data;
    }

    public static byte[] Text(string content) => Encoding.UTF8.GetBytes(content);

    private static byte[] Zip(Action<ZipArchive> build)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            build(zip);
        return output.ToArray();
    }

    private static void Entry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    // Χρωματιστή εικόνα (ώστε να μη συμπιέζεται σε "τίποτα" και να έχει νόημα το crop)
    private static VipsImage Gradient(int width, int height, int shift = 0)
    {
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 3;
            pixels[i] = (byte)(x * 255 / width);
            pixels[i + 1] = (byte)((y * 255 / height + shift) % 256);
            pixels[i + 2] = 120;
        }
        return VipsImage.NewFromMemory(pixels, width, height, 3, Enums.BandFormat.Uchar);
    }
}
