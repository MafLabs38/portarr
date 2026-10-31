using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;
using Portarr.Core.Portal;

namespace Portarr.Core.Accounts;

/// <summary>An avatar file on disk, with what it is served as and the hash the URL carries.</summary>
public sealed record AvatarFile(string Path, string ContentType, string Hash);

public interface IAvatarService
{
    /// <summary>Stores the image as the user's avatar: png, webp or jpeg by its signature, 1 MiB at most. An svg is refused.</summary>
    Task<AccountResult<AccountUser>> SaveAsync(AccountActor actor, int userId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    Task<AccountResult<AccountUser>> RemoveAsync(AccountActor actor, int userId, CancellationToken cancellationToken);

    /// <summary>The file behind a stored file name, or null when it is not a safe, existing avatar file.</summary>
    AvatarFile? Find(string? avatarFile);
}

/// <summary>
/// User avatars (V7, D-95), stored like the portal logo (D-91): checked by content, written under a name made of the
/// user and the image's hash (so the URL's <c>?v=</c> changes with the image and can be cached for a year), served with
/// <c>nosniff</c> and the icons' policy. Unlike the logo, never an SVG: an avatar is user-supplied and shown to others.
/// </summary>
public sealed class AvatarService : IAvatarService
{
    public const long MaxBytes = PortalLogo.MaxBytes;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    private readonly IAccountService _accounts;
    private readonly string _folder;

    public AvatarService(IAccountService accounts, IOptions<PortarrOptions> options)
    {
        _accounts = accounts;
        _folder = Path.GetFullPath(options.Value.AvatarsFolder);
    }

    /// <summary>The extension and content type of an image, by its signature; null for anything else (an svg, a gif, text).</summary>
    public static (string Extension, string ContentType)? Sniff(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(PngSignature))
        {
            return ("png", "image/png");
        }

        if (content.StartsWith(JpegSignature))
        {
            return ("jpg", "image/jpeg");
        }

        return content.Length > 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8) ? ("webp", "image/webp") : null;
    }

    public async Task<AccountResult<AccountUser>> SaveAsync(AccountActor actor, int userId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (content.Length is 0 or > (int)MaxBytes)
        {
            return AccountResult<AccountUser>.Fail(AccountError.Invalid, $"The image must be between 1 byte and {MaxBytes / 1024} KiB.");
        }

        if (Sniff(content.Span) is not { } format)
        {
            return AccountResult<AccountUser>.Fail(AccountError.Invalid, "The image must be a png, webp or jpeg file.");
        }

        var user = await _accounts.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountResult<AccountUser>.Fail(AccountError.NotFound, "No such user.");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(content.Span))[..16];
        var name = $"u{userId}-{hash}.{format.Extension}";
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, name);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, content.ToArray(), cancellationToken);
        File.Move(temporary, path, overwrite: true);

        var result = await _accounts.SetAvatarAsync(actor, userId, name, cancellationToken);
        if (result.Succeeded)
        {
            DeleteOld(user.AvatarFile, name);
        }

        return result;
    }

    public async Task<AccountResult<AccountUser>> RemoveAsync(AccountActor actor, int userId, CancellationToken cancellationToken)
    {
        var user = await _accounts.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountResult<AccountUser>.Fail(AccountError.NotFound, "No such user.");
        }

        var result = await _accounts.SetAvatarAsync(actor, userId, null, cancellationToken);
        if (result.Succeeded)
        {
            DeleteOld(user.AvatarFile, null);
        }

        return result;
    }

    public AvatarFile? Find(string? avatarFile)
    {
        if (string.IsNullOrEmpty(avatarFile) || avatarFile != Path.GetFileName(avatarFile))
        {
            return null;
        }

        var path = Path.Combine(_folder, avatarFile);
        var extension = Path.GetExtension(avatarFile).TrimStart('.');
        var contentType = extension switch { "png" => "image/png", "jpg" => "image/jpeg", "webp" => "image/webp", _ => null };
        if (contentType is null || !File.Exists(path))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(avatarFile);
        return new AvatarFile(path, contentType, stem[(stem.LastIndexOf('-') + 1)..]);
    }

    private void DeleteOld(string? previous, string? keep)
    {
        if (previous is not null && previous != keep && previous == Path.GetFileName(previous))
        {
            File.Delete(Path.Combine(_folder, previous));
        }
    }
}
