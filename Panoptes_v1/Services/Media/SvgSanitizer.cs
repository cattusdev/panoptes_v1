using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Panoptes_v1.Services.Media
{
    // Καθαρίζει SVG (όταν επιτρέπεται): κρατά μόνο γνωστά στοιχεία σχεδίασης, πετά scripts, event handlers,
    // foreignObject, animations και εξωτερικά links. Επιπλέον σερβίρεται πάντα με CSP sandbox.
    public static partial class SvgSanitizer
    {
        private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
        private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

        private static readonly HashSet<string> AllowedElements = new(StringComparer.Ordinal)
        {
            "svg", "g", "defs", "title", "desc", "symbol", "use", "path", "rect", "circle", "ellipse", "line",
            "polyline", "polygon", "text", "tspan", "textPath", "linearGradient", "radialGradient", "stop",
            "clipPath", "mask", "pattern", "marker", "filter", "style", "image", "feBlend", "feColorMatrix",
            "feComponentTransfer", "feComposite", "feFlood", "feGaussianBlur", "feMerge", "feMergeNode",
            "feMorphology", "feOffset", "feDropShadow", "feFuncR", "feFuncG", "feFuncB", "feFuncA",
        };

        public static byte[] Sanitize(string path)
        {
            XDocument document;
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, // Όχι entities (XXE, "billion laughs")
                    XmlResolver = null,
                    MaxCharactersInDocument = 20_000_000,
                };
                using var reader = XmlReader.Create(path, settings);
                document = XDocument.Load(reader);
            }
            catch (XmlException)
            {
                throw new MediaValidationException("The file isn't a valid SVG image.");
            }

            var root = document.Root;
            if (root == null || root.Name != Svg + "svg")
                throw new MediaValidationException("The file isn't a valid SVG image.");

            Clean(root);

            var output = new MemoryStream();
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                document.Save(writer);
            return output.ToArray();
        }

        private static void Clean(XElement element)
        {
            foreach (var attribute in element.Attributes().ToList())
            {
                if (!IsSafeAttribute(element, attribute))
                    attribute.Remove();
            }

            if (element.Name.LocalName == "style" && UnsafeCss().IsMatch(element.Value))
                element.Value = "";

            foreach (var child in element.Elements().ToList())
            {
                if (child.Name.Namespace != Svg || !AllowedElements.Contains(child.Name.LocalName))
                    child.Remove();
                else
                    Clean(child);
            }
        }

        private static bool IsSafeAttribute(XElement element, XAttribute attribute)
        {
            if (attribute.IsNamespaceDeclaration) return true;

            var name = attribute.Name;
            var value = WhiteSpace().Replace(attribute.Value, "");

            if (name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)) return false;
            if (value.Contains("javascript:", StringComparison.OrdinalIgnoreCase)) return false;

            if (name.LocalName == "href" && (name.Namespace == XNamespace.None || name.Namespace == XLink))
            {
                // Μόνο εσωτερικές αναφορές (#id) ή ενσωματωμένες εικόνες σε <image>
                return value.StartsWith('#') ||
                       (element.Name.LocalName == "image" && EmbeddedImage().IsMatch(value));
            }

            if (name.Namespace != XNamespace.None && name.Namespace != XNamespace.Xml) return false;
            if (name.LocalName == "style" && UnsafeCss().IsMatch(attribute.Value)) return false;
            return true;
        }

        [GeneratedRegex(@"@import|url\s*\(\s*['""]?\s*(?!#)|expression\s*\(|javascript:", RegexOptions.IgnoreCase)]
        private static partial Regex UnsafeCss();

        [GeneratedRegex(@"^data:image/(png|jpeg|gif|webp);base64,", RegexOptions.IgnoreCase)]
        private static partial Regex EmbeddedImage();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhiteSpace();
    }
}
