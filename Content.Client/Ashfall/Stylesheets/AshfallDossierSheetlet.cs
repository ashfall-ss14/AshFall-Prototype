using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Ashfall.Client.Stylesheets;

/// <summary>
///     Typography and interactive rules for the lifepath / personnel dossier screens.
///     Visual language: PT Serif voice against RobotoMono service labels; no boxes.
///     Keeps XAML structure-only: all looks live here, mirroring the HTML/CSS split.
/// </summary>
[CommonSheetlet]
public sealed class AshfallDossierSheetlet : Sheetlet<AshfallStylesheet>
{
    public const string DisplayClass = "AshfallDossierDisplay";
    public const string DisplaySubtleClass = "AshfallDossierDisplaySubtle";
    public const string SerifClass = "AshfallDossierSerif";
    public const string SerifItalicClass = "AshfallDossierSerifItalic";
    public const string LabelClass = "AshfallDossierLabel";
    public const string ChipClass = "AshfallDossierChip";
    public const string OptionCardClass = "AshfallDossierOptionCard";
    public const string OptionTitleClass = "AshfallDossierOptionTitle";
    public const string CandidateNameClass = "AshfallCandidateName";
    public const string LedgerStepClass = "AshfallLedgerStep";
    public const string LedgerValueClass = "AshfallLedgerValue";

    public const string WeaknessClass = "AshfallDossierWeakness";

    public override StyleRule[] GetRules(AshfallStylesheet sheet, object config)
    {
        var displayFont = ResCache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 20);
        var candidateFont = ResCache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 16);
        var titleFont = ResCache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 15);
        var bodyFont = ResCache.GetFont("/Fonts/Tahoma/tahoma.ttf", 14);
        var monoLabelFont = ResCache.GetFont("/Fonts/RobotoMono/RobotoMono-Bold.ttf", 11);
        var monoSmall = ResCache.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 11);
        var weaknessFont = ResCache.GetFont("/Fonts/Tahoma/tahoma.ttf", 13);

        // Tactile dark option cards with clear borders and warm amber hover
        var optionNormal = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#10141CE6"),
            BorderThickness = new Thickness(1),
            BorderColor = Color.FromHex("#222936"),
        };
        optionNormal.SetContentMarginOverride(StyleBox.Margin.Horizontal, 18);
        optionNormal.SetContentMarginOverride(StyleBox.Margin.Vertical, 12);

        var optionHover = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#18202CF0"),
            BorderThickness = new Thickness(1),
            BorderColor = Color.FromHex("#8C6A3E"),
        };
        optionHover.SetContentMarginOverride(StyleBox.Margin.Horizontal, 18);
        optionHover.SetContentMarginOverride(StyleBox.Margin.Vertical, 12);

        var optionPressed = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#131720F0"),
            BorderThickness = new Thickness(1),
            BorderColor = Color.FromHex("#C89248"),
        };
        optionPressed.SetContentMarginOverride(StyleBox.Margin.Horizontal, 18);
        optionPressed.SetContentMarginOverride(StyleBox.Margin.Vertical, 12);

        return new StyleRule[]
        {
            // Prompt question: bold, prominent, high contrast.
            E<Label>().Class(DisplayClass)
                .Prop(Label.StylePropertyFont, displayFont)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#F5F2EA")),
            // Candidate name on identity plate.
            E<Label>().Class(CandidateNameClass)
                .Prop(Label.StylePropertyFont, candidateFont)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#F5F2EA")),
            // Subtle descriptions.
            E<Label>().Class(DisplaySubtleClass)
                .Prop(Label.StylePropertyFont, bodyFont)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#8A92A0")),
            // Main body voice for descriptions and choices.
            E<Label>().Class(SerifClass)
                .Prop(Label.StylePropertyFont, bodyFont)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#D6D1C4")),
            // Weakness highlighting: clear toned-down amber.
            E<Label>().Class(WeaknessClass)
                .Prop(Label.StylePropertyFont, weaknessFont)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#E6923C")),
            // Quoted flavor lines.
            E<Label>().Class(SerifItalicClass)
                .Prop(Label.StylePropertyFont, bodyFont)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#B0AAA0")),
            // Mono service labels: warm legible bronze.
            E<Label>().Class(LabelClass)
                .Prop(Label.StylePropertyFont, monoLabelFont)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#B89E78")),
            // Ledger markers (chronicle of choices).
            E<Label>().Class(LedgerStepClass)
                .Prop(Label.StylePropertyFont, monoLabelFont)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#C88A3E")),
            E<Label>().Class(LedgerValueClass)
                .Prop(Label.StylePropertyFont, bodyFont)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#E2DDD2")),
            E<Label>().Class(ChipClass)
                .Prop(Label.StylePropertyFont, monoSmall),
            // Option titles: crisp white, prominent.
            E<Label>().Class(OptionTitleClass)
                .Prop(Label.StylePropertyFont, titleFont)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#FFFFFF")),

            ButtonRule(OptionCardClass).PseudoNormal()
                .Prop(ContainerButton.StylePropertyStyleBox, optionNormal),
            ButtonRule(OptionCardClass).PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, optionHover),
            ButtonRule(OptionCardClass).PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, optionPressed),
            ButtonRule(OptionCardClass).PseudoHovered()
                .ParentOf(E<Label>().Class(OptionTitleClass))
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#F0A84E")),
        };
    }

    private static MutableSelectorElement ButtonRule(string styleClass)
    {
        return E<ContainerButton>()
            .Class(ContainerButton.StyleClassButton)
            .Class(styleClass);
    }
}
