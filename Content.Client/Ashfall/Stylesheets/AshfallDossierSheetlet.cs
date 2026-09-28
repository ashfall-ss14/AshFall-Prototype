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

    public override StyleRule[] GetRules(AshfallStylesheet sheet, object config)
    {
        var pixelDisplay = ResCache.GetFont("/Fonts/Cozette/CozetteVectorBold.ttf", 16);
        var pixelCandidate = ResCache.GetFont("/Fonts/Cozette/CozetteVectorBold.ttf", 14);
        var pixelSubtle = ResCache.GetFont("/Fonts/Cozette/CozetteVector.ttf", 12);
        var pixelTitle = ResCache.GetFont("/Fonts/Cozette/CozetteVectorBold.ttf", 13);
        var pixelBody = ResCache.GetFont("/Fonts/Cozette/CozetteVector.ttf", 12);
        var pixelItalic = ResCache.GetFont("/Fonts/Cozette/CozetteVectorItalic.ttf", 12);
        var pixelSmall = ResCache.GetFont("/Fonts/Cozette/CozetteVector.ttf", 11);

        // Lifepath option rows: tactile dark pixel cards with amber glow on hover
        var optionNormal = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#0B0D10D0"),
            BorderThickness = new Thickness(1),
            BorderColor = Color.FromHex("#1B1F26"),
        };
        optionNormal.SetContentMarginOverride(StyleBox.Margin.Horizontal, 14);
        optionNormal.SetContentMarginOverride(StyleBox.Margin.Vertical, 8);

        var optionHover = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#13171FEE"),
            BorderThickness = new Thickness(1),
            BorderColor = Color.FromHex("#5E4B32"),
        };
        optionHover.SetContentMarginOverride(StyleBox.Margin.Horizontal, 14);
        optionHover.SetContentMarginOverride(StyleBox.Margin.Vertical, 8);

        var optionPressed = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#0F1217EE"),
            BorderThickness = new Thickness(1),
            BorderColor = Color.FromHex("#8C6B3E"),
        };
        optionPressed.SetContentMarginOverride(StyleBox.Margin.Horizontal, 14);
        optionPressed.SetContentMarginOverride(StyleBox.Margin.Vertical, 8);

        return new StyleRule[]
        {
            // Prompt question: prominent retro header.
            E<Label>().Class(DisplayClass)
                .Prop(Label.StylePropertyFont, pixelDisplay)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#E6E2D8")),
            // Candidate name on identity plate.
            E<Label>().Class(CandidateNameClass)
                .Prop(Label.StylePropertyFont, pixelCandidate)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#E6E2D8")),
            // Quiet pixel text for sub-names and ghost states.
            E<Label>().Class(DisplaySubtleClass)
                .Prop(Label.StylePropertyFont, pixelSubtle)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#6B7380")),
            // Pixel body voice for introspective descriptions and records.
            E<Label>().Class(SerifClass)
                .Prop(Label.StylePropertyFont, pixelBody)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#9B968B")),
            // Quoted flavor lines.
            E<Label>().Class(SerifItalicClass)
                .Prop(Label.StylePropertyFont, pixelItalic)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#88837A")),
            // Small pixel service labels.
            E<Label>().Class(LabelClass)
                .Prop(Label.StylePropertyFont, pixelSmall)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#666D77")),
            // Ledger markers (chronicle of choices).
            E<Label>().Class(LedgerStepClass)
                .Prop(Label.StylePropertyFont, pixelSmall)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#C87D38")),
            E<Label>().Class(LedgerValueClass)
                .Prop(Label.StylePropertyFont, pixelBody)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#D6D0C2")),
            E<Label>().Class(ChipClass)
                .Prop(Label.StylePropertyFont, pixelSmall),
            // Option titles: sharp amber/bone pixel font.
            E<Label>().Class(OptionTitleClass)
                .Prop(Label.StylePropertyFont, pixelTitle)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#DCD7CA")),

            ButtonRule(OptionCardClass).PseudoNormal()
                .Prop(ContainerButton.StylePropertyStyleBox, optionNormal),
            ButtonRule(OptionCardClass).PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, optionHover),
            ButtonRule(OptionCardClass).PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, optionPressed),
            ButtonRule(OptionCardClass).PseudoHovered()
                .ParentOf(E<Label>().Class(OptionTitleClass))
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#E69C3C")),
        };
    }

    private static MutableSelectorElement ButtonRule(string styleClass)
    {
        return E<ContainerButton>()
            .Class(ContainerButton.StyleClassButton)
            .Class(styleClass);
    }
}
