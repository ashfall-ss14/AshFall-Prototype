using Content.Client.ContextMenu.UI;
using Content.Client.Examine;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
using Content.Client.Stylesheets.Sheetlets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using System.Numerics;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Ashfall.Client.Stylesheets;

[CommonSheetlet]
public sealed class AshfallCoreSheetlet : Sheetlet<AshfallStylesheet>
{
    public override StyleRule[] GetRules(AshfallStylesheet sheet, object config)
    {
        // Classic Win95 3D buttons everywhere; graphite bodies for regular actions, amber for primary.
        var buttonNormal = TexBox("/Textures/Interface/Ashfall/btn-gray-normal.png", 8, 4, patch: 5);
        var buttonHover = TexBox("/Textures/Interface/Ashfall/btn-gray-hover.png", 8, 4, patch: 5);
        var buttonPressed = TexBox("/Textures/Interface/Ashfall/btn-gray-pressed.png", 8, 4, patch: 5);
        var buttonDisabled = TexBox("/Textures/Interface/Ashfall/btn-gray-disabled.png", 8, 4, patch: 5);

        // Tactile industrial surfaces: chassis panels, recessed deep surfaces, windows.
        var panel = TexBox("/Textures/Interface/Ashfall/panel-frame.png", 10, 8, patch: 3, tile: true);
        var panelDeep = TexBox("/Textures/Interface/Ashfall/panel-frame-deep.png", 10, 8, patch: 3, tile: true);
        var lobbyPanel = TexBox("/Textures/Interface/Ashfall/panel-frame.png", 8, 7, patch: 3, tile: true);
        var lobbyInset = TexBox("/Textures/Interface/Ashfall/panel-frame-deep.png", 8, 5, patch: 3, tile: true);
        var lobbyHeader = TexBox("/Textures/Interface/Ashfall/header-rust.png", 10, 6, patch: 4, tile: true);
        var lobbyChat = TexBox("/Textures/Interface/Ashfall/panel-frame-deep.png", 6, 6, patch: 3, tile: true);
        var lineEdit = TexBox("/Textures/Interface/Ashfall/input-sunken.png", 6, 3, patch: 5);
        var windowHeader = TexBox("/Textures/Interface/Ashfall/header-rust.png", 6, 4, patch: 4, tile: true);
        var windowBackground = TexBox("/Textures/Interface/Ashfall/panel-frame.png", 8, 8, patch: 3, tile: true);
        var tooltipBox = TexBox("/Textures/Interface/Nano/tooltip.png", 8, 6, patch: 2);
        var chatBg = new StyleBoxFlat(Color.FromHex("#25252ADD"));
        var contextMenuBox = Box(Color.FromHex("#151617"), AshfallStylesheet.Border, 2, 2);
        var tabs = Box(AshfallStylesheet.PanelDeep, AshfallStylesheet.Border, 2, 2);
        var monoBold = ResCache.GetFont("/Fonts/RobotoMono/RobotoMono-Bold.ttf", 13);
        var monoSmall = ResCache.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 11);
        // Retro-OS body font (WinXp pack): denser and more readable than the Noto default.
        // Class-based rules below (mono headers, button fonts) still override it where set.
        var tahoma = ResCache.GetFont("/Fonts/Tahoma/tahoma.ttf", 12);
        var tahomaBold = ResCache.GetFont("/Fonts/Tahoma/tahoma.ttf", 13);

        var rules = new List<StyleRule>
        {
            E<Label>().Prop(Label.StylePropertyFont, tahoma),
            E<RichTextLabel>().Prop(Label.StylePropertyFont, tahoma),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonNormal),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).PseudoNormal()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonNormal),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonHover),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonPressed),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).PseudoDisabled()
                .Prop(ContainerButton.StylePropertyStyleBox, buttonDisabled),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleClass.ButtonOpenLeft)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonNormal),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleClass.ButtonOpenRight)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonNormal),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleClass.ButtonOpenBoth)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonNormal),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(StyleClass.ButtonSquare)
                .Prop(ContainerButton.StylePropertyStyleBox, buttonNormal),

            E<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, lineEdit)
                .Prop("font-color", AshfallStylesheet.Text),

            E<PanelContainer>().Class(StyleClass.BackgroundPanel).Panel(panel),
            E<PanelContainer>().Class(StyleClass.BackgroundPanelDark).Panel(panelDeep),
            E<PanelContainer>().Class(AshfallStylesheet.PanelClass).Panel(panel),
            E<PanelContainer>().Class(AshfallStylesheet.PanelDeepClass).Panel(panelDeep),
            E<PanelContainer>().Class(AshfallStylesheet.LobbyPanelClass).Panel(lobbyPanel),
            E<PanelContainer>().Class(AshfallStylesheet.LobbyInsetClass).Panel(lobbyInset),
            E<PanelContainer>().Class(AshfallStylesheet.LobbyHeaderClass).Panel(lobbyHeader),
            E<PanelContainer>().Class(AshfallStylesheet.LobbyChatPanelClass).Panel(lobbyChat),
            E<PanelContainer>().Class(ChatInputBox.StyleClassChatPanel).Panel(chatBg),
            E<OutputPanel>().Class(AshfallStylesheet.ChatLogPanelClass)
                .Prop(OutputPanel.StylePropertyStyleBox, new StyleBoxEmpty()),
            E<Content.Client.Ashfall.UI.Chat.Controls.CustomOutputPanel>()
                .Prop(Content.Client.Ashfall.UI.Chat.Controls.CustomOutputPanel.StylePropertyFont, tahoma),
            // Retro-web header strip: warm gradient with an amber underline, tiled 1:1 so the
            // grain never stretches on wide panels.
            E<PanelContainer>().Class(AshfallStylesheet.HeaderPanelClass)
                .Panel(TexBox("/Textures/Interface/Ashfall/header-rust.png", 10, 6, patch: 4, tile: true)),

            // Windows
            E().Class(DefaultWindow.StyleClassWindowPanel).Panel(windowBackground),
            E().Class(DefaultWindow.StyleClassWindowHeader).Panel(windowHeader),
            E<Label>().Class(DefaultWindow.StyleClassWindowTitle)
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(13, FontKind.Bold))
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.Orange),
            E<Label>().Class("FancyWindowTitle")
                .Prop(Label.StylePropertyFont, monoBold)
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.Orange),
            E().Class(StyleClass.BorderedWindowPanel).Panel(windowBackground),

            // Tooltips & Examine Popup
            E<PanelContainer>().Class(StyleClass.TooltipPanel).Panel(tooltipBox),
            E<PanelContainer>().Class(ExamineSystem.StyleClassEntityTooltip).Panel(tooltipBox),
            E<Tooltip>().Prop(Tooltip.StylePropertyPanel, tooltipBox),
            E<RichTextLabel>().Class(StyleClass.TooltipTitle)
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(13, FontKind.Bold))
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.Text),
            E<RichTextLabel>().Class(StyleClass.TooltipDesc)
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(12))
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.MutedText),

            // Context Menu
            E<PanelContainer>().Class(ContextMenuPopup.StyleClassContextMenuPopup).Panel(contextMenuBox),

            E<TabContainer>()
                .Prop(TabContainer.StylePropertyPanelStyleBox, tabs)
                .Prop(TabContainer.StylePropertyTabStyleBox, Box(AshfallStylesheet.PrimaryAmber, AshfallStylesheet.PrimaryAmber, 8, 4))
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, Box(AshfallStylesheet.Panel, AshfallStylesheet.Border, 8, 4)),

            E<Label>().Class(AshfallStylesheet.TerminalHeaderClass)
                .Prop(Label.StylePropertyFont, monoBold)
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.Text),
            E<Label>().Class(AshfallStylesheet.SectionHeaderClass)
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(14, FontKind.Bold))
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.Orange),
            E<Label>().Class(AshfallStylesheet.StatusClass)
                .Prop(Label.StylePropertyFont, monoSmall)
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.Teal),
            E<Label>().Class(AshfallStylesheet.MutedClass)
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.MutedText),
            E<RichTextLabel>().Class(AshfallStylesheet.MutedClass)
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.MutedText),
            E<Label>().Class(AshfallStylesheet.WarningClass)
                .Prop(Label.StylePropertyFont, monoSmall)
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.Orange),
        };

        // Classic Win95 3D buttons; light text on the dark amber primary, faded text on graphite secondaries.
        // Disabled bodies stay colored, just dimmed: a dead-black button reads as a rendering bug.
        var disabledButton = Box(Color.FromHex("#1A1B1D"), Color.FromHex("#222426"), 8, 4);
        var primaryDisabled = Box(Color.FromHex("#3B3024"), Color.FromHex("#765431"), 8, 4);
        AddFlatButtonRules(rules, AshfallStylesheet.PrimaryActionClass,
            "btn-amber", Color.FromHex("#F0C987"), Color.FromHex("#FFE0A6"), Color.FromHex("#E6BF7D"),
            Color.FromHex("#D8C3A2"), primaryDisabled,
            sheet.BaseFont.GetFont(16, FontKind.Bold));
        // Ready toggle: amber while not ready, teal body while ready (toggled = :pressed),
        // so the two states never read the same.
        rules.AddRange(new StyleRule[]
        {
            ButtonRule(AshfallStylesheet.ReadyActionClass).PseudoNormal()
                .Prop(ContainerButton.StylePropertyStyleBox, TexBox("/Textures/Interface/Ashfall/btn-amber-normal.png", 8, 4, patch: 5)),
            ButtonRule(AshfallStylesheet.ReadyActionClass).PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, TexBox("/Textures/Interface/Ashfall/btn-amber-hover.png", 8, 4, patch: 5)),
            ButtonRule(AshfallStylesheet.ReadyActionClass).PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, Box(Color.FromHex("#6FA79C"), Color.FromHex("#9AC9C0"), 8, 4)),
            ButtonRule(AshfallStylesheet.ReadyActionClass).PseudoDisabled()
                .Prop(ContainerButton.StylePropertyStyleBox, primaryDisabled),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(AshfallStylesheet.ReadyActionClass)
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(16, FontKind.Bold))
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#F0C987")),
            ButtonRule(AshfallStylesheet.ReadyActionClass).PseudoPressed()
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#10201D")),
            ButtonRule(AshfallStylesheet.ReadyActionClass).PseudoDisabled()
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#D8C3A2")),
        });
        AddFlatButtonRules(rules, AshfallStylesheet.AccentActionClass,
            "btn-accent", Color.FromHex("#A3A8A3"), Color.FromHex("#D48944"), Color.FromHex("#A3A8A3"),
            Color.FromHex("#545654"), disabledButton,
            sheet.BaseFont.GetFont(14, FontKind.Bold));
        AddFlatButtonRules(rules, AshfallStylesheet.SecondaryActionClass,
            "btn-gray", Color.FromHex("#C6C9C4"), Color.FromHex("#E8EAE6"), Color.FromHex("#A3A8A3"),
            Color.FromHex("#545654"), disabledButton,
            sheet.BaseFont.GetFont(14, FontKind.Bold));
        AddFlatButtonRules(rules, AshfallStylesheet.NavigationActionClass,
            "btn-gray", Color.FromHex("#C6C9C4"), Color.FromHex("#E8EAE6"), Color.FromHex("#A3A8A3"),
            Color.FromHex("#545654"), disabledButton,
            sheet.BaseFont.GetFont(12));
        AddFlatButtonRules(rules, AshfallStylesheet.UtilityActionClass,
            "btn-gray", Color.FromHex("#C6C9C4"), Color.FromHex("#E8EAE6"), Color.FromHex("#A3A8A3"),
            Color.FromHex("#545654"), disabledButton,
            sheet.BaseFont.GetFont(12));

        // Secondary text actions used inside dense records. They remain clearly interactive on
        // hover and press without competing with the dossier content like a full bevel button.
        StyleBoxFlat InlineActionBox(Color background)
        {
            var box = new StyleBoxFlat { BackgroundColor = background };
            box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 5);
            box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
            return box;
        }

        rules.AddRange(new StyleRule[]
        {
            ButtonRule(AshfallStylesheet.InlineActionClass).PseudoNormal()
                .Prop(ContainerButton.StylePropertyStyleBox, InlineActionBox(Color.Transparent)),
            ButtonRule(AshfallStylesheet.InlineActionClass).PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, InlineActionBox(Color.FromHex("#343638"))),
            ButtonRule(AshfallStylesheet.InlineActionClass).PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, InlineActionBox(Color.FromHex("#151617"))),
            ButtonRule(AshfallStylesheet.InlineActionClass).PseudoDisabled()
                .Prop(ContainerButton.StylePropertyStyleBox, InlineActionBox(Color.Transparent)),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(AshfallStylesheet.InlineActionClass)
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFont, monoSmall)
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.Text),
            ButtonRule(AshfallStylesheet.InlineActionClass).PseudoHovered()
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.Orange),
            ButtonRule(AshfallStylesheet.InlineActionClass).PseudoPressed()
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#B97A3F")),
            ButtonRule(AshfallStylesheet.InlineActionClass).PseudoDisabled()
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.DisabledText),
        });
        AddFlatButtonRules(rules, AshfallStylesheet.DestructiveActionClass,
            "btn-gray", Color.FromHex("#C1705F"), Color.FromHex("#D4836F"), Color.FromHex("#8E463D"),
            Color.FromHex("#545654"), disabledButton,
            sheet.BaseFont.GetFont(12, FontKind.Bold));

        // Assignment rows / list entries: graphite surfaces with a hover lift, selected = warm tint
        // + amber border instead of a bright fill.
        var listNormal = Box(Color.FromHex("#1B1C1E"), Color.FromHex("#2A2C2E"), 8, 4);
        var listHover = Box(Color.FromHex("#242628"), Color.FromHex("#3D3F42"), 8, 4);
        var listPressed = Box(Color.FromHex("#151617"), Color.FromHex("#2A2C2E"), 8, 4);
        var listSelected = new StyleBoxFlat
        {
            BackgroundColor = AshfallStylesheet.SelectedSurface,
            BorderColor = AshfallStylesheet.SelectedBorder,
            BorderThickness = new Thickness(2),
        };
        listSelected.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        listSelected.SetContentMarginOverride(StyleBox.Margin.Vertical, 4);
        rules.AddRange(new StyleRule[]
        {
            ButtonRule(AshfallStylesheet.ListItemClass).PseudoNormal()
                .Prop(ContainerButton.StylePropertyStyleBox, listNormal),
            ButtonRule(AshfallStylesheet.ListItemClass).PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, listHover),
            ButtonRule(AshfallStylesheet.ListItemClass).PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, listPressed),
            ButtonRule(AshfallStylesheet.ListItemClass).PseudoDisabled()
                .Prop(ContainerButton.StylePropertyStyleBox, listNormal),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(AshfallStylesheet.ListItemClass)
                .Class(AshfallStylesheet.ListItemSelectedClass)
                .Prop(ContainerButton.StylePropertyStyleBox, listSelected),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(AshfallStylesheet.ListItemClass)
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(12)),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(AshfallStylesheet.ListItemClass)
                .PseudoDisabled()
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFontColor, AshfallStylesheet.DisabledText),
        });

        return rules.ToArray();
    }

    private static MutableSelectorElement ButtonRule(string styleClass)
    {
        return E<ContainerButton>()
            .Class(ContainerButton.StyleClassButton)
            .Class(styleClass);
    }

    private static StyleBoxFlat Box(Color background, Color border, float horizontal, float vertical)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(1),
        };
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontal);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, vertical);
        return box;
    }

    private void AddFlatButtonRules(
        List<StyleRule> rules,
        string styleClass,
        string textureBase,
        Color text,
        Color hoverText,
        Color pressedText,
        Color disabledText,
        StyleBoxFlat disabledBox,
        Font font)
    {
        // Beveled 9-slice with a flat center: retro tactile edges, clean surface for text.
        StyleBoxTexture Bevel(string state) =>
            TexBox($"/Textures/Interface/Ashfall/{textureBase}-{state}.png", 8, 4, patch: 5);

        rules.AddRange(new StyleRule[]
        {
            ButtonRule(styleClass).PseudoNormal()
                .Prop(ContainerButton.StylePropertyStyleBox, Bevel("normal")),
            ButtonRule(styleClass).PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, Bevel("hover")),
            ButtonRule(styleClass).PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, Bevel("pressed")),
            ButtonRule(styleClass).PseudoDisabled()
                .Prop(ContainerButton.StylePropertyStyleBox, disabledBox),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(styleClass)
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFont, font)
                .Prop(Label.StylePropertyFontColor, text),
            ButtonRule(styleClass).PseudoHovered()
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFontColor, hoverText),
            ButtonRule(styleClass).PseudoPressed()
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFontColor, pressedText),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(styleClass).PseudoDisabled()
                .ParentOf(E<Label>())
                .Prop(Label.StylePropertyFontColor, disabledText),
        });
    }

    private StyleBoxTexture TexBox(string texture, float horizontal, float vertical, int patch = 2, bool tile = false,
        float scale = 1f)
    {
        var box = new StyleBoxTexture
        {
            Texture = ResCache.GetResource<TextureResource>(texture),
            // The brushed-metal source is seamless: tiling keeps the grain at a constant fine
            // scale on any surface size, stretching it would smear the streaks over big panels.
            Mode = tile ? StyleBoxTexture.StretchMode.Tile : StyleBoxTexture.StretchMode.Stretch,
            TextureScale = new Vector2(scale, scale),
            PatchMarginLeft = patch,
            PatchMarginRight = patch,
            PatchMarginTop = patch,
            PatchMarginBottom = patch,
        };
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontal);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, vertical);
        return box;
    }
}
