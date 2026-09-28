using System.Numerics;
using Content.Client.Message;
using Content.Shared.Ashfall.CharacterGen;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Roles;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Ashfall.CharacterGen.UI.Lifepath;

public sealed partial class AshfallLobbySlotCard : PanelContainer
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private static readonly Color IdleBg = Color.FromHex("#151618");
    private static readonly Color IdleBorder = Color.Transparent;

    private static readonly Color HoverBg = Color.FromHex("#221D17");
    private static readonly Color HoverBorder = Color.FromHex("#E5984C");

    private static readonly Color SelectedBorder = Color.FromHex("#D48944");
    private static readonly Color LockedBorder = Color.FromHex("#7D2B2B");

    private PanelContainer CardPanel => this.FindControl<PanelContainer>("CardPanel");
    private Label SlotNumberLabel => this.FindControl<Label>("SlotNumberLabel");
    private PanelContainer StatusBadgePanel => this.FindControl<PanelContainer>("StatusBadgePanel");
    private Label StatusBadgeLabel => this.FindControl<Label>("StatusBadgeLabel");
    private PanelContainer PreviewContainer => this.FindControl<PanelContainer>("PreviewContainer");
    private ProfileFullBodySpriteView PreviewSprite => this.FindControl<ProfileFullBodySpriteView>("PreviewSprite");
    private BoxContainer EmptyContainer => this.FindControl<BoxContainer>("EmptyContainer");
    private Button EmptySlotButton => this.FindControl<Button>("EmptySlotButton");
    private BoxContainer InfoContainer => this.FindControl<BoxContainer>("InfoContainer");
    private Label NameLabel => this.FindControl<Label>("NameLabel");
    private PanelContainer JobColorBar => this.FindControl<PanelContainer>("JobColorBar");
    private Label JobLabel => this.FindControl<Label>("JobLabel");
    private Label BioLabel => this.FindControl<Label>("BioLabel");
    private Label FlawLabel => this.FindControl<Label>("FlawLabel");
    private BoxContainer ActionsContainer => this.FindControl<BoxContainer>("ActionsContainer");
    private Button SelectButton => this.FindControl<Button>("SelectButton");
    private Button ResetButton => this.FindControl<Button>("ResetButton");

    private readonly StyleBoxFlat _cardStyle = new()
    {
        BackgroundColor = IdleBg,
        BorderColor = IdleBorder,
        BorderThickness = new Thickness(1),
    };

    public int SlotIndex { get; private set; }
    public bool IsOccupied { get; private set; }
    public AshfallSlotStatus Status { get; private set; } = AshfallSlotStatus.Empty;
    public bool IsSelected { get; private set; }

    private bool _isHovered;
    private float _hoverProgress;

    public event Action<int>? SlotClicked;
    public event Action<int>? SelectPressed;
    public event Action<int>? ResetPressed;

    public AshfallLobbySlotCard()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        CardPanel.PanelOverride = _cardStyle;

        EmptySlotButton.OnPressed += _ => SlotClicked?.Invoke(SlotIndex);
        SelectButton.OnPressed += _ => SelectPressed?.Invoke(SlotIndex);
        ResetButton.OnPressed += _ => ResetPressed?.Invoke(SlotIndex);

        OnMouseEntered += _ => _isHovered = true;
        OnMouseExited += _ => _isHovered = false;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var target = _isHovered ? 1f : 0f;
        if (MathHelper.CloseToPercent(_hoverProgress, target))
            return;

        _hoverProgress = MathHelper.Lerp(_hoverProgress, target, Math.Clamp(args.DeltaSeconds * 10f, 0f, 1f));
        UpdateVisuals();
    }

    public void SetSlot(int slotIndex, AshfallClientPinnedSlot? slot, bool isSelected)
    {
        SlotIndex = slotIndex;
        IsSelected = isSelected;
        SlotNumberLabel.Text = $"СЛОТ #{slotIndex + 1}";

        if (slot == null)
        {
            IsOccupied = false;
            Status = AshfallSlotStatus.Empty;
            PreviewSprite.Visible = false;
            EmptyContainer.Visible = true;
            EmptySlotButton.Visible = true;
            InfoContainer.Visible = false;
            ActionsContainer.Visible = false;

            StatusBadgeLabel.Text = "ВАКАНТНО";
            SetBadgeColor(Color.FromHex("#1F2124"), Color.FromHex("#3B3E44"), Color.FromHex("#707780"));
        }
        else
        {
            IsOccupied = true;
            Status = slot.Status;
            PreviewSprite.Visible = true;
            EmptyContainer.Visible = false;
            EmptySlotButton.Visible = false;
            InfoContainer.Visible = true;
            ActionsContainer.Visible = true;

            var profile = slot.Candidate.Profile;
            JobPrototype? job = _prototypes.TryIndex(slot.Job, out var j) ? j : null;
            PreviewSprite.LoadPreview(profile, job, showClothes: true);

            NameLabel.Text = profile.Name;
            var jobName = job != null ? Loc.GetString(job.Name) : slot.Job.Id;
            JobLabel.Text = jobName;

            var speciesName = _prototypes.TryIndex<SpeciesPrototype>(profile.Species, out var sp)
                ? Loc.GetString(sp.Name)
                : profile.Species.Id;
            BioLabel.Text = $"{speciesName}, {profile.Age} лет";

            // Department color
            var depColor = GetDepartmentColor(slot.Candidate.PrimaryDomain);
            ((StyleBoxFlat)JobColorBar.PanelOverride!).BackgroundColor = depColor;
            JobLabel.FontColorOverride = depColor;

            // Flaw / personality note from dossier; plain Label, so markup tags must be stripped
            var flawSection = slot.Candidate.Dossier.Sections.Find(s => s.Kind == "personality" || s.Kind == "hook");
            FlawLabel.Text = flawSection != null && flawSection.Lines.Count > 0
                ? FormattedMessage.RemoveMarkupPermissive(flawSection.Lines[0])
                : string.Empty;

            // Configure status & buttons
            switch (slot.Status)
            {
                case AshfallSlotStatus.OnShift:
                    StatusBadgeLabel.Text = Loc.GetString("ashfall-lobby-slot-status-onshift");
                    SetBadgeColor(Color.FromHex("#3D1616"), Color.FromHex("#7D2B2B"), Color.FromHex("#E06A6A"));
                    ResetButton.Disabled = true;
                    ResetButton.ToolTip = Loc.GetString("ashfall-lobby-slot-locked-hint");
                    SelectButton.Text = Loc.GetString("ashfall-lobby-slot-button-selected");
                    SelectButton.Disabled = true;
                    break;

                case AshfallSlotStatus.Evacuated:
                    StatusBadgeLabel.Text = Loc.GetString("ashfall-lobby-slot-status-evacuated");
                    SetBadgeColor(Color.FromHex("#16283D"), Color.FromHex("#2A527D"), Color.FromHex("#5B9CE0"));
                    ResetButton.Disabled = false;
                    ResetButton.ToolTip = string.Empty;
                    SelectButton.Text = isSelected
                        ? Loc.GetString("ashfall-lobby-slot-button-selected")
                        : Loc.GetString("ashfall-lobby-slot-button-select");
                    SelectButton.Disabled = isSelected;
                    break;

                case AshfallSlotStatus.Dead:
                    StatusBadgeLabel.Text = Loc.GetString("ashfall-lobby-slot-status-dead");
                    SetBadgeColor(Color.FromHex("#2B2222"), Color.FromHex("#543D3D"), Color.FromHex("#B08B8B"));
                    ResetButton.Disabled = false;
                    ResetButton.ToolTip = string.Empty;
                    SelectButton.Text = Loc.GetString("ashfall-lobby-slot-button-select");
                    SelectButton.Disabled = true;
                    break;

                case AshfallSlotStatus.Ready:
                default:
                    StatusBadgeLabel.Text = Loc.GetString("ashfall-lobby-slot-status-ready");
                    SetBadgeColor(Color.FromHex("#1E2D1F"), Color.FromHex("#355437"), Color.FromHex("#6DB372"));
                    // A created candidate is pinned: a new one only comes after a played shift or death.
                    ResetButton.Disabled = true;
                    ResetButton.ToolTip = Loc.GetString("ashfall-lobby-slot-reset-locked");
                    SelectButton.Text = isSelected
                        ? Loc.GetString("ashfall-lobby-slot-button-selected")
                        : Loc.GetString("ashfall-lobby-slot-button-select");
                    SelectButton.Disabled = isSelected;
                    break;
            }
        }

        UpdateVisuals();
    }

    private void SetBadgeColor(Color bg, Color border, Color text)
    {
        // Minimal chips: colored mono text, no box, no outline.
        StatusBadgePanel.PanelOverride = new StyleBoxFlat { BackgroundColor = Color.Transparent };
        StatusBadgeLabel.FontColorOverride = text;
    }

    private static Color GetDepartmentColor(string domain)
    {
        return domain switch
        {
            "Engineering" => Color.FromHex("#D48944"),
            "Medical" => Color.FromHex("#3B82A6"),
            "Security" => Color.FromHex("#B83C3C"),
            "Service" => Color.FromHex("#3FA85B"),
            _ => Color.FromHex("#8C9299"),
        };
    }

    private void UpdateVisuals()
    {
        var baseBg = Color.InterpolateBetween(IdleBg, HoverBg, _hoverProgress);
        var baseBorder = Color.InterpolateBetween(IdleBorder, HoverBorder, _hoverProgress);

        if (IsSelected)
        {
            baseBorder = Color.InterpolateBetween(SelectedBorder, HoverBorder, _hoverProgress);
            _cardStyle.BorderThickness = new Thickness(2);
        }
        else if (Status == AshfallSlotStatus.OnShift)
        {
            baseBorder = Color.InterpolateBetween(LockedBorder, HoverBorder, _hoverProgress);
            _cardStyle.BorderThickness = new Thickness(1);
        }
        else
        {
            _cardStyle.BorderThickness = new Thickness(1);
        }

        _cardStyle.BackgroundColor = baseBg;
        _cardStyle.BorderColor = baseBorder;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // Spotlight accent highlight
        if (_hoverProgress > 0.02f)
        {
            var center = PixelSize / 2f;
            var radius = Math.Max(PixelSize.X, PixelSize.Y) * 0.55f;
            var glowColor = new Color(229, 152, 76, (byte)(38 * _hoverProgress));
            handle.DrawCircle(center, radius, glowColor);
        }
    }
}
