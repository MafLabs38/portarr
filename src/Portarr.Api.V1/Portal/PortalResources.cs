using FluentValidation;
using Portarr.Core.Portal;

namespace Portarr.Api.V1.Portal;

/// <summary>
/// The identity of the portal (D-91). <c>LogoUrl</c> is null when Portarr's own logo applies; otherwise it carries a
/// hash of the file, so a browser fetches a new logo as soon as it changes. <c>Version</c> is the running Portarr's.
/// <c>LogoProblem</c> says why a declared logo is not used. <c>Writable</c>: whether the name and logo can be changed
/// through the API. <c>Colors</c>: the colors of the name and of the version in the badge (a hex color or a theme token
/// name), Portarr's accent by default.
/// </summary>
public sealed record PortalResource(string Name, string? LogoUrl, string Version, string? LogoProblem, bool Writable, PortalColorsResource Colors);

/// <summary>The colors of the badge texts: <c>#rgb</c>, <c>#rrggbb</c> or a theme token name (accent, accentDeep, secondary, ink, inkSoft, inkMuted).</summary>
public sealed record PortalColorsResource(string Name, string Version);

/// <summary>The colors to set: null leaves one as it is, an empty text goes back to the default.</summary>
public sealed record PortalColorsRequest(string? Name, string? Version);

/// <summary>
/// The name and/or the colors to set: <c>Version</c> is the catalog version you read (the current one when omitted:
/// the identity is all the request changes); an empty <c>Name</c> goes back to Portarr's own. At least one of
/// <c>Name</c> and <c>Colors</c> is required.
/// </summary>
public sealed record PortalRequest(string? Version, string? Name, PortalColorsRequest? Colors = null);

public sealed class PortalRequestValidator : AbstractValidator<PortalRequest>
{
    public PortalRequestValidator()
    {
        RuleFor(r => r.Name).NotNull().When(r => r.Colors is null).WithMessage("name or colors is required: the name of your infrastructure (empty to use Portarr's) and/or the colors of the badge.");
        RuleFor(r => r.Colors!.Name)
            .Must(color => string.IsNullOrWhiteSpace(color) || PortalColor.IsValid(color.Trim()))
            .When(r => r.Colors is not null)
            .WithMessage(r => PortalColor.Validate("colors.name", r.Colors!.Name!.Trim()));
        RuleFor(r => r.Colors!.Version)
            .Must(color => string.IsNullOrWhiteSpace(color) || PortalColor.IsValid(color.Trim()))
            .When(r => r.Colors is not null)
            .WithMessage(r => PortalColor.Validate("colors.version", r.Colors!.Version!.Trim()));
        RuleFor(r => r.Name)
            .Must(name => PortalLogo.ValidateName(name!.Trim()) is null)
            .When(r => r.Name is not null)
            .WithMessage(r => PortalLogo.ValidateName(r.Name!.Trim()));
    }
}

public static class PortalResourceMapper
{
    public const string LogoPath = "/api/v1/portal/logo";

    public static PortalResource ToResource(this PortalIdentity identity, string version, bool writable)
    {
        return new PortalResource(
            identity.Name,
            identity.LogoFile is null ? null : $"{LogoPath}?v={identity.LogoFile.Hash}",
            version,
            identity.LogoProblem,
            writable,
            new PortalColorsResource(identity.Colors.Name, identity.Colors.Version));
    }
}
