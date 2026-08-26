namespace Yorii_Launcher.Helpers
{
    public enum AccentBrushRole
    {
        Base,
        Light1,
        Light2,
        Light3,
        Dark1,
        Dark2,
        Dark3,
        TextOnBase
    }

    // accent-driven resource keys mapped to the shade of the generated palette that feeds them
    // two override layers work together
    // 1. systemaccentcolor* colors. winui's built-in control templates derive every accent
    // surface (slider value/thumb, listviewitem/comboboxitem/gridviewitem selection
    // checkbox, radiobutton, toggleswitch, progressbar, textbox selection highlight, ...)
    // from the systemaccentcolor / systemaccentcolorlight1..3 / dark1..3 color resources
    // through its own resource chain (including staticresource aliases such as
    // slidertrackvaluefill -> systemcontrolhighlightaccentbrush -> systemaccentcolor)
    // overriding these seven colors at app level therefore themes *all* framework controls
    // at once, with the framework's own ratios, including the alias leaves that a
    // brush-only override cannot reach. these are updated by replacing the resource value
    // at runtime (winui re-evaluates themeresource expressions on dictionary changes)
    // 2. brushes. the custom accentcolor* keys are referenced directly from app xaml and are
    // mutated in place so every live themeresource reference updates without re-theming
    // a small set of framework leaf keys (slider, selection surfaces) is also overridden
    // with brushes as a guaranteed live-update fallback in case the color replacement above
    // is not picked up by a specific control
    public static class AccentResourceMap
    {
        public const string AccentColor = "AccentColorBrush";
        public const string AccentColorLight1 = "AccentColorLight1Brush";
        public const string AccentColorLight2 = "AccentColorLight2Brush";
        public const string AccentColorLight3 = "AccentColorLight3Brush";
        public const string AccentColorDark1 = "AccentColorDark1Brush";
        public const string AccentColorDark2 = "AccentColorDark2Brush";
        public const string AccentColorDark3 = "AccentColorDark3Brush";
        public const string AccentTextColor = "AccentTextColorBrush";

        // framework color resources that the whole winui accent ramp derives from
        public static readonly (string Key, AccentBrushRole Role)[] SystemColorKeys =
        {
            ("SystemAccentColor", AccentBrushRole.Base),
            ("SystemAccentColorLight1", AccentBrushRole.Light1),
            ("SystemAccentColorLight2", AccentBrushRole.Light2),
            ("SystemAccentColorLight3", AccentBrushRole.Light3),
            ("SystemAccentColorDark1", AccentBrushRole.Dark1),
            ("SystemAccentColorDark2", AccentBrushRole.Dark2),
            ("SystemAccentColorDark3", AccentBrushRole.Dark3),
        };

        public static readonly (string Key, AccentBrushRole Role)[] Brushes =
        {
            (AccentColor, AccentBrushRole.Base),
            (AccentColorLight1, AccentBrushRole.Light1),
            (AccentColorLight2, AccentBrushRole.Light2),
            (AccentColorLight3, AccentBrushRole.Light3),
            (AccentColorDark1, AccentBrushRole.Dark1),
            (AccentColorDark2, AccentBrushRole.Dark2),
            (AccentColorDark3, AccentBrushRole.Dark3),
            (AccentTextColor, AccentBrushRole.TextOnBase),

            // accentbuttonstyle surfaces. pressed uses the darker shade on purpose (the
            // framework's own pressed state is a neutral gray, which reads poorly for a
            // custom accent)
            ("AccentButtonBackground", AccentBrushRole.Base),
            ("AccentButtonBackgroundPointerOver", AccentBrushRole.Base),
            ("AccentButtonBackgroundPressed", AccentBrushRole.Dark1),
            ("AccentFillColorTertiaryBrush", AccentBrushRole.Light2),
            ("SystemControlHighlightAccentBrush", AccentBrushRole.Base),
            ("SystemControlForegroundAccentBrush", AccentBrushRole.Base),
            ("SystemControlBackgroundAccentBrush", AccentBrushRole.Base),
            ("SystemControlHyperlinkTextBrush", AccentBrushRole.Base),

            // slider (value track + thumb). the framework templates consume these leaf keys
            // via staticresource aliases; overriding them here as mutable brushes guarantees
            // the slider recolors even if the systemaccentcolor* replacement is not picked up
            ("SliderThumbBackground", AccentBrushRole.Base),
            ("SliderTrackValueFill", AccentBrushRole.Base),
            ("SliderTrackValueFillPointerOver", AccentBrushRole.Base),
            ("SliderTrackValueFillPressed", AccentBrushRole.Base),

            // list-type selection/hover/contentdialog-border surfaces are intentionally left at
            // framework defaults. selection still picks up the accent through the color ramp
            // (systemcontrolhighlightlistaccentlowbrush -> systemaccentcolor), while hover keeps
            // the stock neutral highlight instead of an over-strong solid accent

            // toggle / choice controls
            ("ToggleSwitchFillOn", AccentBrushRole.Base),
            ("ToggleSwitchFillOnPointerOver", AccentBrushRole.Base),
            ("ToggleSwitchStrokeOnPointerOver", AccentBrushRole.Base),
            ("CheckBoxCheckBackgroundFillChecked", AccentBrushRole.Base),
            ("CheckBoxCheckBackgroundFillCheckedPointerOver", AccentBrushRole.Base),
            ("RadioButtonOuterEllipseCheckedStroke", AccentBrushRole.Base),
            ("RadioButtonOuterEllipseCheckedStrokePointerOver", AccentBrushRole.Base),
            ("ToggleButtonBackgroundChecked", AccentBrushRole.Base),
            ("ToggleButtonBackgroundCheckedPointerOver", AccentBrushRole.Base),

            // navigationview selection indicator + app-bar toggle checked states
            ("NavigationViewSelectionIndicatorForeground", AccentBrushRole.Base),
            ("AppBarToggleButtonBackgroundChecked", AccentBrushRole.Base),
            ("AppBarToggleButtonBackgroundCheckedPointerOver", AccentBrushRole.Base),
            ("AppBarToggleButtonBackgroundCheckedPointerOver", AccentBrushRole.Base),
            ("AppBarToggleButtonBackgroundCheckedPressed", AccentBrushRole.Base),

            // stroke on accent surfaces (textbox accent underline, accent button borders)
            ("ControlStrokeColorOnAccentDefaultBrush", AccentBrushRole.Base),
        };
    }
}