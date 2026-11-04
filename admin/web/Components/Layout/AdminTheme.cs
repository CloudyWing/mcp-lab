using MudBlazor;

namespace CloudyWing.McpLab.Admin.Components.Layout;

/// <summary>
/// Theme shared with AiAccountManager: white and red in light mode, black and orange in dark mode.
/// </summary>
public static class AdminTheme {
    /// <summary>
    /// Gets the theme.
    /// </summary>
    public static MudTheme Theme { get; } = new() {
        PaletteLight = new PaletteLight {
            Background = "#ffffff",
            Surface = "#f6f6f7",
            DrawerBackground = "#f8f8f8",
            AppbarBackground = "#ffffff",
            AppbarText = "#1f2937",
            TextPrimary = "#1f2937",
            TextSecondary = "#4b5563",
            Divider = "rgba(176, 33, 33, .15)",
            LinesDefault = "#c2c2c4",
            LinesInputs = "#c2c2c4",
            Primary = "#dc2626",
            PrimaryContrastText = "#ffffff",
            Secondary = "#b02121",
            Tertiary = "#d97706",
        },
        PaletteDark = new PaletteDark {
            Background = "#1c1b1f",
            Surface = "#161618",
            DrawerBackground = "#26242a",
            AppbarBackground = "#202127",
            AppbarText = "#f8fafc",
            TextPrimary = "#f8fafc",
            TextSecondary = "#a8a29e",
            Divider = "rgba(248, 250, 252, .12)",
            LinesDefault = "#3c3f44",
            LinesInputs = "#3c3f44",
            Primary = "#fb923c",
            PrimaryContrastText = "#111827",
            Secondary = "#f97316",
            Tertiary = "#d97706",
        },
    };
}
