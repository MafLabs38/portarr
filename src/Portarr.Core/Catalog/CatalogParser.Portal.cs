using Portarr.Core.Portal;

namespace Portarr.Core.Catalog;

// The identity of the portal (D-91): a name, a logo and the colors of the badge texts, all optional.
public static partial class CatalogParser
{
    private sealed partial class Builder
    {
        private CatalogPortal? BuildPortal(PortalDocument? document)
        {
            if (document is null)
            {
                return null;
            }

            var name = Clean(document.Name);
            var logo = Clean(document.Logo);
            var colorName = Clean(document.Colors?.Name);
            var colorVersion = Clean(document.Colors?.Version);

            var errors = new[]
            {
                name is null ? null : PortalLogo.ValidateName(name),
                logo is null ? null : PortalLogo.ValidatePath(logo),
                colorName is null ? null : PortalColor.Validate("colors.name", colorName),
                colorVersion is null ? null : PortalColor.Validate("colors.version", colorVersion),
            }.OfType<string>().ToList();
            _errors.AddRange(errors.Select(error => $"portal: {error}"));

            var colors = colorName is null && colorVersion is null ? null : new CatalogPortalColors(colorName, colorVersion);
            return errors.Count > 0 || (name is null && logo is null && colors is null) ? null : new CatalogPortal(name, logo, colors);
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
