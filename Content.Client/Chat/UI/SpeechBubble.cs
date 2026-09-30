using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client.Ashfall.Chat;
using Content.Client.Chat.Managers;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Speech;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Chat.UI
{
    public abstract partial class SpeechBubble : Control
    {
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private IEyeManager _eyeManager = default!;
        [Dependency] protected IEntityManager _entityManager = default!;
        [Dependency] protected IConfigurationManager ConfigManager = default!;
        [Dependency] protected IPrototypeManager _prototypeManager = default!;
        [Dependency] protected IResourceCache _resourceCache = default!;
        private readonly SharedTransformSystem _transformSystem;

        public enum SpeechType : byte
        {
            Emote,
            Say,
            Whisper,
            Looc
        }

        /// <summary>
        ///     The total time a speech bubble stays on screen.
        /// </summary>
        private static readonly TimeSpan TotalTime = TimeSpan.FromSeconds(4);

        /// <summary>
        ///     The amount of time at the end of the bubble's life at which it starts fading.
        /// </summary>
        private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(0.25f);

        /// <summary>
        ///     The distance in world space to offset the speech bubble from the center of the entity.
        ///     i.e. greater -> higher above the mob's head.
        /// </summary>
        private const float EntityVerticalOffset = 0.5f;

        /// <summary>
        ///     The default maximum width for speech bubbles.
        /// </summary>
        public const float SpeechMaxWidth = 256;

        private readonly EntityUid _senderEntity;

        /// <summary>
        /// The time at which this bubble will die.
        /// </summary>
        private TimeSpan _deathTime;

        private bool _dying;
        private bool _dead;

        public float VerticalOffset { get; set; }
        protected float VerticalOffsetAchieved;

        public Vector2 ContentSize { get; private set; }

        // man down
        public event Action<EntityUid, SpeechBubble>? OnDied;

        public static SpeechBubble CreateSpeechBubble(SpeechType type, ChatMessage message, EntityUid senderEntity)
        {
            if (IoCManager.Resolve<IConfigurationManager>().GetCVar(CCVars.ChatEnableRunechatBubbles))
                return new RunechatSpeechBubble(type, message, senderEntity);

            switch (type)
            {
                case SpeechType.Emote:
                    return new TextSpeechBubble(message, senderEntity, "emoteBox");

                case SpeechType.Say:
                    return new FancyTextSpeechBubble(message, senderEntity, "sayBox");

                case SpeechType.Whisper:
                    return new FancyTextSpeechBubble(message, senderEntity, "whisperBox");

                case SpeechType.Looc:
                    return new TextSpeechBubble(message, senderEntity, "emoteBox", Color.FromHex("#48d1cc"));

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public SpeechBubble(ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null, TimeSpan? totalTime = null)
        {
            IoCManager.InjectDependencies(this);
            _senderEntity = senderEntity;
            _transformSystem = _entityManager.System<SharedTransformSystem>();
            MouseFilter = MouseFilterMode.Ignore;

            // Use text clipping so new messages don't overlap old ones being pushed up.
            RectClipContent = true;

            var bubble = BuildBubble(message, speechStyleClass, fontColor);

            AddChild(bubble);

            ForceRunStyleUpdate();

            bubble.Measure(Vector2Helpers.Infinity);
            ContentSize = bubble.DesiredSize;
            VerticalOffsetAchieved = -ContentSize.Y;
            _deathTime = _timing.RealTime + (totalTime ?? TotalTime);
        }

        protected abstract Control BuildBubble(ChatMessage message, string speechStyleClass, Color? fontColor = null);

        protected virtual Vector2 GetWorldPositionOffset(EntityUid senderEntity, TransformComponent xform)
        {
            return Vector2.Zero;
        }

        protected virtual Vector2 GetScreenPositionOffset(EntityUid senderEntity, TransformComponent xform)
        {
            return Vector2.Zero;
        }

        protected virtual float GetSenderVisibilityAlpha()
        {
            var alpha = 1f;

            if (_entityManager.TryGetComponent<SpriteComponent>(_senderEntity, out var sprite))
            {
                if (!sprite.Visible && _entityManager.IsClientSide(_senderEntity))
                    return 0f;

                alpha = sprite.Color.A;
            }

            if (_entityManager.TryGetComponent<StealthComponent>(_senderEntity, out var stealth) && stealth.Enabled)
            {
                var stealthSys = _entityManager.System<SharedStealthSystem>();
                var stealthAlpha = Math.Clamp(stealthSys.GetVisibility(_senderEntity, stealth), 0f, 1f);
                alpha = Math.Min(alpha, stealthAlpha);
            }

            return alpha;
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            var timeLeft = (float)(_deathTime - _timing.RealTime).TotalSeconds;
            if (_entityManager.Deleted(_senderEntity) || timeLeft <= 0)
            {
                // Timer spawn to prevent concurrent modification exception.
                if (!_dying)
                {
                    _dying = true;
                    Timer.Spawn(0, Die);
                }
                return;
            }

            // Lerp to our new vertical offset if it's been modified.
            if (MathHelper.CloseToPercent(VerticalOffsetAchieved - VerticalOffset, 0, 0.1))
            {
                VerticalOffsetAchieved = VerticalOffset;
            }
            else
            {
                VerticalOffsetAchieved = MathHelper.Lerp(VerticalOffsetAchieved, VerticalOffset, 10 * args.DeltaSeconds);
            }

            if (!_entityManager.TryGetComponent<TransformComponent>(_senderEntity, out var xform) || xform.MapID != _eyeManager.CurrentEye.Position.MapId)
            {
                Modulate = Color.White.WithAlpha(0);
                return;
            }

            var alpha = GetSenderVisibilityAlpha();

            if (timeLeft <= FadeTime.TotalSeconds)
            {
                // Update alpha if we're fading.
                Modulate = Color.White.WithAlpha((timeLeft / (float)FadeTime.TotalSeconds) * alpha);
            }
            else
            {
                // Make opaque otherwise, because it might have been hidden before
                Modulate = Color.White.WithAlpha(alpha);
            }

            var baseOffset = 0f;

            if (_entityManager.TryGetComponent<SpeechComponent>(_senderEntity, out var speech))
                baseOffset = speech.SpeechBubbleOffset;

            var offset = (-_eyeManager.CurrentEye.Rotation).ToWorldVec() * -(EntityVerticalOffset + baseOffset);
            var worldPos = _transformSystem.GetWorldPosition(xform) + offset + GetWorldPositionOffset(_senderEntity, xform);

            var lowerCenter = _eyeManager.WorldToScreen(worldPos) / UIScale + GetScreenPositionOffset(_senderEntity, xform);
            var screenPos = lowerCenter - new Vector2(ContentSize.X / 2, ContentSize.Y + VerticalOffsetAchieved);
            // Round to nearest 0.5
            screenPos = (screenPos * 2).Rounded() / 2;
            LayoutContainer.SetPosition(this, screenPos);

            if (!RectClipContent)
            {
                SetHeight = ContentSize.Y;
            }
            else
            {
                var height = MathF.Ceiling(MathHelper.Clamp(lowerCenter.Y - screenPos.Y, 0, ContentSize.Y));
                SetHeight = height;
            }
        }

        private void Die()
        {
            if (Disposed || _dead)
            {
                return;
            }

            _dead = true;
            OnDied?.Invoke(_senderEntity, this);
            OnDied = null;
        }

        /// <summary>
        ///     Causes the speech bubble to start fading IMMEDIATELY.
        /// </summary>
        public void FadeNow()
        {
            if (_deathTime > _timing.RealTime)
            {
                _deathTime = _timing.RealTime + FadeTime;
            }
        }

        protected FormattedMessage FormatSpeech(string message, Color? fontColor = null)
        {
            var msg = new FormattedMessage();
            if (fontColor != null)
                msg.PushColor(fontColor.Value);
            msg.AddMarkupOrThrow(message);
            return msg;
        }

        protected FormattedMessage ExtractAndFormatSpeechSubstring(ChatMessage message, string tag, Color? fontColor = null)
        {
            return FormatSpeech(SharedChatSystem.GetStringInsideTag(message, tag), fontColor);
        }

        protected bool TryGetLanguageIcon(ChatMessage message, [NotNullWhen(true)] out Texture? texture)
        {
            texture = null;

            if (string.IsNullOrEmpty(message.LanguageIcon))
                return false;

            if (_resourceCache.TryGetResource<TextureResource>(new ResPath(message.LanguageIcon), out var textureResource))
            {
                texture = textureResource.Texture;
                return true;
            }

            if (_entityManager.EntitySysManager.TryGetEntitySystem<SpriteSystem>(out var spriteSystem))
            {
                try
                {
                    var specifier = new SpriteSpecifier.Rsi(new ResPath(message.LanguageIcon), "icon");
                    texture = spriteSystem.Frame0(specifier);
                    return true;
                }
                catch
                {
                    // ignored
                }
            }

            return false;
        }
    }

    public sealed class TextSpeechBubble : SpeechBubble
    {
        public TextSpeechBubble(ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
            : base(message, senderEntity, speechStyleClass, fontColor)
        {
        }

        protected override Control BuildBubble(ChatMessage message, string speechStyleClass, Color? fontColor = null)
        {
            var label = new RichTextLabel
            {
                MaxWidth = SpeechMaxWidth,
                OutlineColorOverride = TextOutline.Default.Color,
            };

            label.SetMessage(FormatSpeech(message.WrappedMessage, fontColor), tagsAllowed: null);

            var panel = new PanelContainer
            {
                StyleClasses = { "speechBox", speechStyleClass },
                Children = { label },
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity))
            };

            return panel;
        }
    }

    public sealed class FancyTextSpeechBubble : SpeechBubble
    {
        public FancyTextSpeechBubble(ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
            : base(message, senderEntity, speechStyleClass, fontColor)
        {
        }

        protected override Control BuildBubble(ChatMessage message, string speechStyleClass, Color? fontColor = null)
        {
            if (speechStyleClass == "sayBox" && message.SpeechStyleClass != null)
            {
                speechStyleClass = message.SpeechStyleClass;
            }

            if (!ConfigManager.GetCVar(CCVars.ChatEnableFancyBubbles))
            {
                var label = new RichTextLabel
                {
                    MaxWidth = SpeechMaxWidth,
                    StyleClasses = { "bubbleContent" },
                    OutlineColorOverride = TextOutline.Default.Color,
                };

                label.SetMessage(ExtractAndFormatSpeechSubstring(message, "BubbleContent", fontColor), tagsAllowed: null);

                Control content = label;
                if (TryGetLanguageIcon(message, out var iconTexture))
                {
                    var container = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
                    var textureRect = new TextureRect
                    {
                        Texture = iconTexture,
                        TextureScale = Vector2.One * 0.5f,
                        VerticalAlignment = VAlignment.Center,
                        Margin = new Thickness(0, 0, 4, 0)
                    };
                    container.AddChild(textureRect);
                    container.AddChild(label);
                    content = container;
                }

                var unfanciedPanel = new PanelContainer
                {
                    StyleClasses = { "speechBox", speechStyleClass },
                    Children = { content },
                    ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity)),
                };
                return unfanciedPanel;
            }

            var bubbleHeader = new RichTextLabel
            {
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleSpeakerOpacity)),
                Margin = new Thickness(2, 0, 2, 0),
                OutlineColorOverride = TextOutline.Default.Color,
            };

            var bubbleContent = new RichTextLabel
            {
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleTextOpacity)),
                MaxWidth = SpeechMaxWidth,
                Margin = new Thickness(2, 0, 2, 0),
                StyleClasses = { "bubbleContent" },
                OutlineColorOverride = TextOutline.Default.Color,
            };

            var headerContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            if (TryGetLanguageIcon(message, out var headerIcon))
            {
                var iconTexture = new TextureRect
                {
                    Texture = headerIcon,
                    TextureScale = Vector2.One * 0.5f,
                    VerticalAlignment = VAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                headerContainer.AddChild(iconTexture);
            }

            bubbleHeader.SetMessage(ExtractAndFormatSpeechSubstring(message, "BubbleHeader", fontColor), tagsAllowed:null);
            headerContainer.AddChild(bubbleHeader);
            bubbleContent.SetMessage(ExtractAndFormatSpeechSubstring(message, "BubbleContent", fontColor), tagsAllowed:null);

            var mainPanel = new PanelContainer
            {
                StyleClasses = { "speechBox", speechStyleClass },
                Children = { bubbleContent },
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity)),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Bottom,
                Margin = new Thickness(4, 20, 4, 2)
            };

            var headerPanel = new PanelContainer
            {
                StyleClasses = { "speechBox", speechStyleClass },
                Children = { headerContainer },
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.ChatFancyNameBackground) ? ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity) : 0f),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Top
            };

            var panel = new PanelContainer
            {
                Children = { mainPanel, headerPanel }
            };

            return panel;
        }
    }
}
