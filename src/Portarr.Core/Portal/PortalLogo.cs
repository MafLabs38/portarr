using System.Text;
using System.Xml;

namespace Portarr.Core.Portal;

/// <summary>What a logo file is, once inspected: the extension it must carry and the content type it is served with.</summary>
public sealed record LogoFormat(string Extension, string ContentType);

/// <summary>
/// The rules for the portal's logo (D-91): where it may live (a relative path inside the catalog folder), which
/// formats it may have (png, webp, svg), how big it may be and whether the bytes are really what the extension says.
/// One place, shared by the catalog parser, the upload and the service that serves the file.
/// </summary>
public static class PortalLogo
{
    /// <summary>Same limit as an icon (D-47): a logo is small.</summary>
    public const long MaxBytes = 1024 * 1024;

    public const int MaxPathLength = 200;
    public const int MaxNameLength = 40;

    /// <summary>The folder next to the catalog that uploads write to.</summary>
    public const string Folder = "branding";

    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly LogoFormat Png = new("png", "image/png");
    private static readonly LogoFormat WebP = new("webp", "image/webp");
    private static readonly LogoFormat Svg = new("svg", "image/svg+xml");
    private static readonly LogoFormat[] Formats = [Png, WebP, Svg];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The formats a logo may have, by extension (png, webp, svg).</summary>
    public static IReadOnlyList<string> Extensions { get; } = Formats.Select(f => f.Extension).ToList();

    /// <summary>The format an extension (with or without its dot, any case) stands for, or null.</summary>
    public static LogoFormat? FormatOfExtension(string extension)
    {
        var name = extension.TrimStart('.');
        return Formats.FirstOrDefault(f => string.Equals(f.Extension, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The name of the infrastructure: text on one line, at most <see cref="MaxNameLength"/> characters. Null when valid.</summary>
    public static string? ValidateName(string name)
    {
        if (name.Length > MaxNameLength)
        {
            return $"name must be at most {MaxNameLength} characters.";
        }

        return name.Any(char.IsControl) ? "name must be one line of text." : null;
    }

    /// <summary>
    /// A logo path as the catalog states it: relative to the catalog folder, forward slashes, no <c>..</c>, no
    /// absolute or drive path, no hidden file, and png, webp or svg. Null when valid.
    /// </summary>
    public static string? ValidatePath(string path)
    {
        if (path.Length > MaxPathLength)
        {
            return $"logo must be at most {MaxPathLength} characters.";
        }

        if (path.StartsWith('/') || path.StartsWith('~') || path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal) || path.Any(char.IsControl))
        {
            return "logo must be a path relative to the catalog folder, with forward slashes (branding/logo.png).";
        }

        if (path.Split('/').Any(segment => segment.Length == 0 || segment.StartsWith('.')))
        {
            return "logo must stay inside the catalog folder: no '..', no empty or hidden path segment.";
        }

        return FormatOfExtension(Path.GetExtension(path)) is null ? $"logo must be a {string.Join(", ", Extensions)} file." : null;
    }

    /// <summary>
    /// The full path of a logo inside <paramref name="catalogFolder"/>, or null when the path is not valid, leaves the
    /// folder (also through a link) or is not a file.
    /// </summary>
    public static string? Resolve(string catalogFolder, string path)
    {
        if (ValidatePath(path) is not null)
        {
            return null;
        }

        var root = Path.GetFullPath(catalogFolder);
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!IsInside(root, full) || !File.Exists(full))
        {
            return null;
        }

        return StaysInside(root, full) ? full : null;
    }

    /// <summary>
    /// Whether walking from the root to the file, resolving every link on the way (a linked folder as well as a linked
    /// file), never leaves the root: a symlinked directory inside the catalog folder cannot lead a logo out of it.
    /// </summary>
    private static bool StaysInside(string root, string full)
    {
        var realRoot = Real(new DirectoryInfo(root));
        var current = realRoot;
        foreach (var segment in Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar))
        {
            var next = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            current = Real(info);
            if (!IsInside(realRoot, current))
            {
                return false;
            }
        }

        return true;
    }

    private static string Real(FileSystemInfo info) => (info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName).TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>
    /// Checks the bytes of a logo against its extension: the signature of a png or a webp, or a well-formed svg without
    /// script, event handler or embedded foreign content. Returns the format, or the reason it is refused.
    /// </summary>
    public static (LogoFormat? Format, string? Error) Inspect(ReadOnlySpan<byte> content, string extension)
    {
        var expected = FormatOfExtension(extension);
        if (expected is null)
        {
            return (null, $"The file must be a {string.Join(", ", Extensions)} image.");
        }

        if (content.Length == 0 || content.Length > MaxBytes)
        {
            return (null, $"The image must be between 1 byte and {MaxBytes / 1024} KiB.");
        }

        return HasValidContent(expected, content) ? (expected, null) : (null, $"The file is not a valid {expected.Extension} image.");
    }

    private static bool HasValidContent(LogoFormat expected, ReadOnlySpan<byte> content)
    {
        if (expected == Png)
        {
            return content.StartsWith(PngSignature);
        }

        if (expected == WebP)
        {
            return content.Length > 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8);
        }

        return IsSafeSvg(content);
    }

    private static bool IsInside(string root, string full)
    {
        return full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsSafeSvg(ReadOnlySpan<byte> content)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes };
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(content.ToArray()), settings);
            var root = true;
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if ((root && (reader.LocalName != "svg" || reader.NamespaceURI != SvgNamespace)) || IsActive(reader))
                    {
                        return false;
                    }

                    root = false;

                    // A style sheet is allowed, but one that loads anything from outside is not. Reading its text moves past it.
                    if (reader.LocalName == "style")
                    {
                        if (LoadsFromOutside(reader.ReadElementContentAsString()))
                        {
                            return false;
                        }

                        continue;
                    }
                }

                reader.Read();
            }

            return !root;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>CSS that imports a sheet or points <c>url()</c> anywhere but at a fragment of the same document.</summary>
    private static bool LoadsFromOutside(string css)
    {
        if (css.Contains("@import", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (var start = css.IndexOf("url(", StringComparison.OrdinalIgnoreCase); start >= 0; start = css.IndexOf("url(", start + 4, StringComparison.OrdinalIgnoreCase))
        {
            var argument = css[(start + 4)..].TrimStart(' ', '"', '\'');
            if (!argument.StartsWith('#'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An element that can run code or load content: a script, a foreign object, an event handler, a script link, a link to another document, a style sheet that loads from outside.</summary>
    private static bool IsActive(XmlReader element)
    {
        if (element.LocalName is "script" or "foreignObject")
        {
            return true;
        }

        for (var more = element.MoveToFirstAttribute(); more; more = element.MoveToNextAttribute())
        {
            var value = element.Value.TrimStart();
            if (element.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)
                || (element.LocalName == "href" && value.Length > 0 && !value.StartsWith('#'))
                || (element.LocalName == "style" && LoadsFromOutside(value)))
            {
                return true;
            }
        }

        element.MoveToElement();
        return false;
    }
}
