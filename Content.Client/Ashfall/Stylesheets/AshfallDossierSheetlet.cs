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
        var serifDisplay = ResCache.GetFont("/Fonts/Ashfall/PTSerif-Bold.ttf", 22);
        var serifCandidate = ResCache.GetFont("/Fonts/Ashfall/PTSerif-Bold.ttf", 17);
        var serifName = ResCache.GetFont("/Fonts/Ashfall/PTSerif-Bold.ttf", 15);
        var serifTitle = ResCache.GetFont("/Fonts/Ashfall/PTSerif-Bold.ttf", 15);
        var serifBody = ResCache.GetFont("/Fonts/Ashfall/PTSerif-Regular.ttf", 13);
        var serifItalic = ResCache.GetFont("/Fonts/Ashfall/PTSerif-Italic.ttf", 12);
        var monoSmall = ResCache.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 10);
        var monoChip = ResCache.GetFont("/Fonts/RobotoMono/RobotoMono-Bold.ttf", 10);

        // Lifepath option rows: tactile dark paper cards with warm amber border glow on hover
        var optionNormal = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#10121400"),
            BorderThickness = new Thickness(1),
            BorderColor = Color.Transparent,
        };
        optionNormal.SetContentMarginOverride(StyleBox.Margin.Horizontal, 18);
        optionNormal.SetContentMarginOverride(StyleBox.Margin.Vertical, 12);

        var optionHover = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#16181BEE"),
            BorderThickness = new Thickness(1),
            BorderColor = Color.FromHex("#3A3125"),
        };
        optionHover.SetContentMarginOverride(StyleBox.Margin.Horizontal, 18);
        optionHover.SetContentMarginOverride(StyleBox.Margin.Vertical, 12);

        var optionPressed = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#121416EE"),
            BorderThickness = new Thickness(1),
            BorderColor = Color.FromHex("#5E452E"),
        };
        optionPressed.SetContentMarginOverride(StyleBox.Margin.Horizontal, 18);
        optionPressed.SetContentMarginOverride(StyleBox.Margin.Vertical, 12);

        return new StyleRule[]
        {
            // Prompt question: prominent, bone-white.
            E<Label>().Class(DisplayClass)
                .Prop(Label.StylePropertyFont, serifDisplay)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#EDE9DE")),
            // Candidate name on identity plate.
            E<Label>().Class(CandidateNameClass)
                .Prop(Label.StylePropertyFont, serifCandidate)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#EDE9DE")),
            // Quiet serif for sub-names and ghost states.
            E<Label>().Class(DisplaySubtleClass)
                .Prop(Label.StylePropertyFont, serifName)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#6A707A")),
            // Serif body voice for introspective descriptions and records.
            E<Label>().Class(SerifClass)
                .Prop(Label.StylePropertyFont, serifBody)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#9E9A8E")),
            // Quoted flavor lines (luggage hooks, weaknesses).
            E<Label>().Class(SerifItalicClass)
                .Prop(Label.StylePropertyFont, serifItalic)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#8C877D")),
            // Small mono service labels.
            E<Label>().Class(LabelClass)
                .Prop(Label.StylePropertyFont, monoSmall)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#707570")),
            // Ledger markers (chronicle of choices).
            E<Label>().Class(LedgerStepClass)
                .Prop(Label.StylePropertyFont, monoSmall)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#B87333")),
            E<Label>().Class(LedgerValueClass)
                .Prop(Label.StylePropertyFont, serifBody)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#D5CFBF")),
            E<Label>().Class(ChipClass)
                .Prop(Label.StylePropertyFont, monoChip),
            // Option voices: the character's thoughts, serif, warm bone.
            E<Label>().Class(OptionTitleClass)
                .Prop(Label.StylePropertyFont, serifTitle)
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#D5CFBF")),

            ButtonRule(OptionCardClass).PseudoNormal()
                .Prop(ContainerButton.StylePropertyStyleBox, optionNormal),
            ButtonRule(OptionCardClass).PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, optionHover),
            ButtonRule(OptionCardClass).PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, optionPressed),
            ButtonRule(OptionCardClass).PseudoHovered()
                .ParentOf(E<Label>().Class(OptionTitleClass))
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#E59838")),
        };
    }

    private static MutableSelectorElement ButtonRule(string styleClass)
    {
        return E<ContainerButton>()
            .Class(ContainerButton.StyleClassButton)
            .Class(styleClass);
    }
}
