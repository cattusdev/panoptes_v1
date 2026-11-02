using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Panoptes_v1.E2E;

// Τα emails της εφαρμογής: στα tests γράφονται ως JSON αρχεία (Email:PickupDirectory) αντί να σταλούν
public static class Mailbox
{
    public static string Directory { get; } = Path.Combine(Path.GetTempPath(), "Panoptes_v1.E2E", "mail");

    public static void Clear()
    {
        if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
        System.IO.Directory.CreateDirectory(Directory);
    }

    public record Email(string To, string Subject, string Html)
    {
        // Το (μοναδικό) κουμπί/link του email
        public string Link => WebUtility.HtmlDecode(Regex.Match(Html, "href=\"([^\"]+)\"").Groups[1].Value);
    }

    public static IReadOnlyList<Email> For(string to) =>
        System.IO.Directory.GetFiles(Directory, "*.json")
            .Order()
            .Select(file => JsonSerializer.Deserialize<Email>(File.ReadAllText(file))!)
            .Where(email => string.Equals(email.To, to, StringComparison.OrdinalIgnoreCase))
            .ToList();

    // Περιμένει το πιο πρόσφατο email για τον παραλήπτη (η αποστολή γίνεται στο ίδιο request, αλλά για σιγουριά)
    public static async Task<Email> WaitForAsync(string to, string subjectContains, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var email = For(to).LastOrDefault(e => e.Subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase));
            if (email != null) return email;
            await Task.Delay(100);
        }
        throw new TimeoutException($"No email '{subjectContains}' arrived for {to}.");
    }
}

// Κωδικοί authenticator app (TOTP, RFC 6238): ίδιος αλγόριθμος με το ASP.NET Identity
public static class Totp
{
    public static string Code(string base32Key)
    {
        var key = Base32Decode(base32Key.Replace(" ", "").ToUpperInvariant());
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}
