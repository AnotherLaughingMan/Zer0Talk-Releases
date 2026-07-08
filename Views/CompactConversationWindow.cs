using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Animation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Zer0Talk.Models;
using Zer0Talk.Services;
using Zer0Talk.Utilities;
using Zer0Talk.ViewModels;

namespace Zer0Talk.Views;

public sealed class CompactConversationWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly string _contactUid;
    private readonly string _displayName;
    private readonly ListBox _messagesList;
    private readonly Popup _composerEmojiPopup;
    private readonly Border _searchHost;
    private readonly Border _pinnedHost;
    private readonly TextBox _searchBox;
    private TextBox? _messageInput;
    private readonly DeliveryStatusToGlyphConverter _deliveryGlyphConverter = new();
    private readonly DeliveryStatusToTooltipConverter _deliveryTooltipConverter = new();
    private readonly DeliveryStatusIsNotReadConverter _deliveryNotReadConverter = new();
    private readonly DeliveryStatusIsReadConverter _deliveryReadConverter = new();
    private bool _isSearchOpen;
    private readonly IBrush _cardBackground;
    private readonly IBrush _cardBorderBrush;
    private readonly WindowLayoutAutosave _layoutAutosave;

    public CompactConversationWindow(string contactUid, string? displayName)
    {
        _contactUid = NormalizeUidKey(contactUid);
        _displayName = string.IsNullOrWhiteSpace(displayName) ? _contactUid : displayName.Trim();

        Title = $"Chat - {_displayName}";
        Width = 700;
        Height = 560;
        MinWidth = 560;
        MinHeight = 420;

        // Custom chrome to match the main window style
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = 32;
        SystemDecorations = SystemDecorations.BorderOnly;
        CanResize = true;

        Background = ResolveBrush("App.Background", Brushes.Black);
        _cardBackground = ResolveBrush("App.CardBackground", ResolveBrush("App.Surface", Brushes.DimGray));
        _cardBorderBrush = ResolveBrush("App.Border", Brushes.Gray);
        _layoutAutosave = new WindowLayoutAutosave(this, GetLayoutCacheKey);
        PositionChanged += (_, __) => _layoutAutosave.ScheduleSave();

        _vm = new MainWindowViewModel();
        DataContext = _vm;

        _messagesList = BuildMessagesList();

        Button emojiButton;
        Grid root;
        (root, emojiButton, _searchHost, _searchBox, _pinnedHost) = BuildContent();

        _composerEmojiPopup = BuildComposerEmojiPopup(emojiButton);
        root.Children.Add(_composerEmojiPopup);

        Content = root;

        TrySelectTargetContact();
        RefreshPinnedHost();

        Opened += (_, __) => RestoreLayoutFromCache();
        Closing += (_, __) => SaveLayoutToCache();
        Closed += (_, __) => _layoutAutosave.Dispose();
        Opened += (_, __) => ScrollToLatestMessage();
        _vm.Messages.CollectionChanged += (_, __) => ScrollToLatestMessage();
        _vm.Messages.CollectionChanged += (_, __) => RefreshPinnedHost();
        _vm.PropertyChanged += OnVmPropertyChanged;
        KeyDown += CompactConversationWindow_KeyDown;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        try
        {
            if (change.Property == BoundsProperty || change.Property == WindowStateProperty)
            {
                _layoutAutosave.ScheduleSave();
            }
        }
        catch { }
    }

    private (Grid Root, Button EmojiButton, Border SearchHost, TextBox SearchBox, Border PinnedHost) BuildContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(0),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto")
        };

        // One continuous top chrome strip. No nested cards, no segmented panels.
        var topChrome = new Border
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Background = ResolveBrush("App.TitleBarBackground", _cardBackground),
            BorderBrush = _cardBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(0)
        };

        var topChromeGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 4
        };

        var dragRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var dragRegion = new Border
        {
            Padding = new Thickness(10, 4, 10, 3),
            Background = Brushes.Transparent,
            Child = dragRow
        };
        dragRegion.AddHandler(InputElement.PointerPressedEvent, CompactDragRegion_PointerPressed, RoutingStrategies.Bubble, true);

        var titleText = new TextBlock
        {
            Text = _displayName,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ResolveBrush("App.ForegroundPrimary", Brushes.White)
        };
        Grid.SetColumn(titleText, 0);

        static Button MakeChromeButton(string glyph, string tip)
        {
            var tb = new TextBlock { Text = glyph, FontSize = 12 };
            tb.Classes.Add("icon-mdl2");
            var btn = new Button { Content = tb, Width = 30, Height = 24, Padding = new Thickness(0) };
            btn.Classes.Add("icon-button");
            ToolTip.SetTip(btn, tip);
            return btn;
        }

        var minBtn = MakeChromeButton("\uE921", "Minimize");
        var maxBtn = MakeChromeButton("\uE922", "Maximize/Restore");
        var closeBtn = MakeChromeButton("\uE8BB", "Close");

        minBtn.Click += (_, __) => WindowState = WindowState.Minimized;
        maxBtn.Click += (_, __) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        closeBtn.Click += (_, __) => Close();

        Grid.SetColumn(minBtn, 1);
        Grid.SetColumn(maxBtn, 2);
        Grid.SetColumn(closeBtn, 3);

        dragRow.Children.Add(titleText);
        dragRow.Children.Add(minBtn);
        dragRow.Children.Add(maxBtn);
        dragRow.Children.Add(closeBtn);

        var contactRowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        var contactRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Avatar
        var avatarBorder = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Background = ResolveBrush("App.Surface", Brushes.DimGray),
            BorderBrush = ResolveBrush("App.Border", Brushes.Gray),
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };

        var avatarGrid = new Grid();
        var avatarFallback = new TextBlock
        {
            Text = "\uE77B",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.4
        };
        avatarFallback.Classes.Add("icon-mdl2");

        var avatarImg = new Avalonia.Controls.Image
        {
            Stretch = Stretch.UniformToFill,
            Width = 38,
            Height = 38
        };
        try
        {
            var bmp = AvatarCache.TryLoad(_contactUid) as Bitmap;
            if (bmp != null)
            {
                avatarImg.Source = bmp;
                avatarFallback.IsVisible = false;
            }
        }
        catch { }

        avatarGrid.Children.Add(avatarFallback);
        avatarGrid.Children.Add(avatarImg);
        avatarBorder.Child = avatarGrid;

        // Name + UID stack
        var nameStack = new StackPanel { Spacing = 1 };
        nameStack.Children.Add(new TextBlock
        {
            Text = _displayName,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Foreground = ResolveBrush("App.ForegroundPrimary", Brushes.White)
        });
        nameStack.Children.Add(new TextBlock
        {
            Text = _contactUid,
            FontSize = 11,
            Opacity = 0.7,
            Foreground = ResolveBrush("App.ForegroundSecondary", Brushes.LightGray)
        });

        contactRow.Children.Add(avatarBorder);
        contactRow.Children.Add(nameStack);
        Grid.SetColumn(contactRow, 0);

        Button MakeHeaderActionButton(string glyph, string tip)
        {
            var text = new TextBlock { Text = glyph, FontSize = 13 };
            text.Classes.Add("icon-mdl2");
            var button = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Classes = { "icon-button" },
                Content = text
            };
            ToolTip.SetTip(button, tip);
            return button;
        }

        var searchBtn = MakeHeaderActionButton("\uE721", "Search");
        searchBtn.Click += SearchBtn_Click;
        Grid.SetColumn(searchBtn, 1);

        var pinBtn = MakeHeaderActionButton("\uE718", "Pinned messages");
        pinBtn.Click += PinBtn_Click;
        Grid.SetColumn(pinBtn, 2);

        var burnBtn = MakeHeaderActionButton("\uEADA", "Burn conversation");
        burnBtn.Content = new Grid
        {
            Width = 16,
            Height = 16,
            Children =
            {
                new Avalonia.Controls.Shapes.Path
                {
                    Fill = ResolveBrush("App.Accent", Brushes.DeepSkyBlue),
                    StrokeThickness = 0,
                    Stretch = Stretch.Uniform,
                    Data = Geometry.Parse("M8,1 C9.5,3 9,4.6 9.4,6 C10.7,5.2 12,6.6 12,8.7 C12,11.1 10.2,13 8,13 C5.8,13 4,11.1 4,8.6 C4,7 4.9,5.8 6.2,5.1 C6.1,6.9 6.9,8.1 7.9,9.0 C8.8,9.9 9.6,9.4 9.6,8.1 C9.6,6.7 8.4,6.1 8,4.7 C7.6,3.4 7.9,2.2 8,1 Z")
                }
            }
        };
        burnBtn.Command = _vm.BurnConversationCommand;
        Grid.SetColumn(burnBtn, 3);

        var encryptedIcon = new TextBlock
        {
            Text = "\uE72E",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8
        };
        encryptedIcon.Classes.Add("icon-mdl2");
        encryptedIcon.Bind(IsVisibleProperty, new Binding(nameof(MainWindowViewModel.IsChatEncrypted)) { Source = _vm });
        ToolTip.SetTip(encryptedIcon, "Encrypted transport");
        Grid.SetColumn(encryptedIcon, 4);

        contactRowGrid.Children.Add(contactRow);
        contactRowGrid.Children.Add(searchBtn);
        contactRowGrid.Children.Add(pinBtn);
        contactRowGrid.Children.Add(burnBtn);
        contactRowGrid.Children.Add(encryptedIcon);

        Grid.SetRow(dragRegion, 0);
        Grid.SetRow(contactRowGrid, 1);
        topChromeGrid.Children.Add(dragRegion);
        topChromeGrid.Children.Add(contactRowGrid);
        topChrome.Child = topChromeGrid;
        Grid.SetRow(topChrome, 0);

        var searchHost = new Border
        {
            IsVisible = false,
            Padding = new Thickness(0),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(0),
            Margin = new Thickness(10, 5, 10, 2)
        };
        Grid.SetRow(searchHost, 1);

        var searchGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 6
        };

        var searchBox = new TextBox
        {
            Watermark = "Search this conversation",
            MinHeight = 30,
            [!TextBox.TextProperty] = new Binding(nameof(MainWindowViewModel.MessageSearchQuery)) { Source = _vm, Mode = BindingMode.TwoWay }
        };
        Grid.SetColumn(searchBox, 0);

        var prevBtn = new Button { Content = new TextBlock { Text = "\uE74B", Classes = { "icon-mdl2" } }, Classes = { "icon-button" }, Width = 28, Height = 28, Command = _vm.PreviousMessageSearchResultCommand };
        ToolTip.SetTip(prevBtn, "Previous result");
        Grid.SetColumn(prevBtn, 1);

        var nextBtn = new Button { Content = new TextBlock { Text = "\uE74C", Classes = { "icon-mdl2" } }, Classes = { "icon-button" }, Width = 28, Height = 28, Command = _vm.NextMessageSearchResultCommand };
        ToolTip.SetTip(nextBtn, "Next result");
        Grid.SetColumn(nextBtn, 2);

        var clearBtn = new Button { Content = new TextBlock { Text = "\uE711", Classes = { "icon-mdl2" } }, Classes = { "icon-button" }, Width = 28, Height = 28, Command = _vm.ClearMessageSearchCommand };
        ToolTip.SetTip(clearBtn, "Clear search");
        Grid.SetColumn(clearBtn, 3);

        searchGrid.Children.Add(searchBox);
        searchGrid.Children.Add(prevBtn);
        searchGrid.Children.Add(nextBtn);
        searchGrid.Children.Add(clearBtn);
        searchHost.Child = searchGrid;

        var pinnedHost = new Border
        {
            IsVisible = false,
            Padding = new Thickness(0),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(0),
            Margin = new Thickness(10, 0, 10, 4)
        };
        Grid.SetRow(pinnedHost, 2);

        // ── Messages list (row 3) ────────────────────────────────────────────────
        Grid.SetRow(_messagesList, 3);

        // ── Composer (row 4) ─────────────────────────────────────────────────────
        var composerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 6,
            Margin = new Thickness(10, 0, 10, 8)
        };

        var input = new TextBox
        {
            Name = "CompactMessageInput",
            Watermark = "Type a message",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 44,
            MaxHeight = 170,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBox.TextProperty] = new Binding(nameof(MainWindowViewModel.OutgoingMessage)) { Mode = BindingMode.TwoWay }
        };
        _messageInput = input;
        input.KeyDown += ComposerInput_KeyDown;
        Grid.SetColumn(input, 0);

        var emojiButton = new Button
        {
            Name = "CompactComposerEmojiPickerButton",
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new TextBlock { Text = "\uE76E" }
        };
        emojiButton.Classes.Add("icon-button");
        ((TextBlock)emojiButton.Content).Classes.Add("icon-mdl2");
        ToolTip.SetTip(emojiButton, "Insert emoji");
        emojiButton.Click += ComposerEmojiPickerButton_Click;
        Grid.SetColumn(emojiButton, 1);

        composerGrid.Children.Add(input);
        composerGrid.Children.Add(emojiButton);
        Grid.SetRow(composerGrid, 4);

        root.Children.Add(topChrome);
        root.Children.Add(searchHost);
        root.Children.Add(pinnedHost);
        root.Children.Add(_messagesList);
        root.Children.Add(composerGrid);

        return (root, emojiButton, searchHost, searchBox, pinnedHost);
    }

    private ListBox BuildMessagesList()
    {
        return new ListBox
        {
            Name = "CompactMessagesList",
            Margin = new Thickness(8, 6, 8, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(MainWindowViewModel.Messages)),
            ItemTemplate = new FuncDataTemplate<Message>((msg, _) =>
            {
                var isMine = string.Equals(NormalizeUidKey(msg.SenderUID), NormalizeUidKey(AppServices.Identity.UID), StringComparison.OrdinalIgnoreCase);
                var sender = isMine ? "You" : _displayName;

                var wrapper = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 10,
                    Background = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top
                };

                var avatarUid = isMine ? AppServices.Identity.UID : msg.SenderUID;
                Bitmap? avatarBitmap = null;
                try { avatarBitmap = AvatarCache.TryLoad(NormalizeUidKey(avatarUid)) as Bitmap; } catch { }

                var avatarFallback = new TextBlock
                {
                    Text = "\uE77B",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.4
                };
                avatarFallback.Classes.Add("icon-mdl2");

                var avatarImage = new Avalonia.Controls.Image
                {
                    Stretch = Stretch.UniformToFill,
                    Source = avatarBitmap,
                    IsVisible = avatarBitmap != null
                };
                if (avatarBitmap != null)
                    avatarFallback.IsVisible = false;

                var avatarBorder = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(18),
                    Background = ResolveBrush("App.Surface", Brushes.DimGray),
                    BorderBrush = ResolveBrush("App.Border", Brushes.Gray),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new Grid
                    {
                        Children = { avatarFallback, avatarImage }
                    }
                };
                Grid.SetColumn(avatarBorder, 0);

                var content = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
                    ColumnSpacing = 8,
                    RowSpacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                Grid.SetColumn(content, 1);

                var senderBlock = new TextBlock
                {
                    Text = sender,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = ResolveBrush("App.ForegroundPrimary", Brushes.White)
                };
                Grid.SetRow(senderBlock, 0);
                Grid.SetColumn(senderBlock, 0);

                var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                metaRow.Children.Add(new TextBlock
                {
                    Text = msg.Timestamp.ToLocalTime().ToString("H:mm"),
                    FontSize = 11,
                    Opacity = 0.65,
                    Foreground = ResolveBrush("App.ForegroundSecondary", Brushes.LightGray)
                });
                if (msg.IsPinned)
                {
                    var pinIcon = new TextBlock { Text = "\uE718", FontSize = 11, Opacity = 0.75 };
                    pinIcon.Classes.Add("icon-mdl2");
                    metaRow.Children.Add(pinIcon);
                }
                if (msg.IsStarred)
                {
                    var starIcon = new TextBlock { Text = "\uE734", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#FFD700")) };
                    starIcon.Classes.Add("icon-mdl2");
                    metaRow.Children.Add(starIcon);
                }
                Grid.SetRow(metaRow, 0);
                Grid.SetColumn(metaRow, 1);

                var hoverActions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0,
                    IsHitTestVisible = false,
                    Transitions = new Transitions
                    {
                        new DoubleTransition
                        {
                            Property = Visual.OpacityProperty,
                            Duration = TimeSpan.FromMilliseconds(200)
                        }
                    }
                };

                Button MakeMessageAction(string glyph, string tip, System.Windows.Input.ICommand? command)
                {
                    var tb = new TextBlock { Text = glyph };
                    tb.Classes.Add("icon-mdl2");
                    var btn = new Button { Content = tb, Width = 24, Height = 24, Padding = new Thickness(0), Classes = { "icon-button" } };
                    ToolTip.SetTip(btn, tip);
                    if (command != null)
                    {
                        btn.Command = command;
                        btn.CommandParameter = msg.Id;
                    }
                    return btn;
                }

                hoverActions.Children.Add(MakeMessageAction("\uE248", "Reply", _vm.ReplyToMessageCommand));
                hoverActions.Children.Add(MakeMessageAction("\uE11D", "React", null));
                hoverActions.Children.Add(MakeMessageAction("\uE718", "Pin / Unpin", _vm.TogglePinMessageCommand));
                hoverActions.Children.Add(MakeMessageAction("\uE734", "Star / Unstar", _vm.ToggleStarMessageCommand));
                if (isMine)
                    hoverActions.Children.Add(MakeMessageAction("\uE74D", "Delete", _vm.DeleteMessageCommand));
                Grid.SetRow(hoverActions, 0);
                Grid.SetColumn(hoverActions, 2);

                var body = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ResolveBrush("App.ForegroundPrimary", Brushes.White)
                };
                body.Bind(TextBlock.TextProperty, new Binding(nameof(Message.RenderedContent)));
                body.Bind(TextBlock.FontSizeProperty, new Binding(nameof(Message.IsEmojiOnly)) { Converter = EmojiOnlyFontSizeConverter.Instance });
                Grid.SetRow(body, 1);
                Grid.SetColumn(body, 0);
                Grid.SetColumnSpan(body, 3);

                var replyPreview = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4),
                    Background = ResolveBrush("App.Surface", Brushes.DimGray),
                    BorderBrush = ResolveBrush("App.Border", Brushes.Gray),
                    BorderThickness = new Thickness(1)
                };
                replyPreview.Bind(IsVisibleProperty, new Binding(nameof(Message.HasReplyMetadata)));
                Grid.SetRow(replyPreview, 2);
                Grid.SetColumn(replyPreview, 0);
                Grid.SetColumnSpan(replyPreview, 3);

                var replyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                replyRow.Children.Add(new TextBlock { Text = "\uE248", Classes = { "icon-mdl2" }, Opacity = 0.75 });
                replyRow.Children.Add(new TextBlock { Text = msg.ReplyToPreview ?? string.Empty, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 560, Opacity = 0.82 });
                replyPreview.Child = replyRow;

                var footerRow = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(footerRow, 3);
                Grid.SetColumn(footerRow, 0);
                Grid.SetColumnSpan(footerRow, 3);

                var editCountdown = new TextBlock { FontSize = 11, Opacity = 0.55 };
                editCountdown.Bind(TextBlock.IsVisibleProperty, new Binding(nameof(Message.IsEdited)));
                editCountdown.Text = "Edited";
                footerRow.Children.Add(editCountdown);

                var deliveryRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (isMine)
                {
                    var outboundGray = new TextBlock { FontSize = 11, Opacity = 0.55 };
                    outboundGray.Classes.Add("icon-mdl2");
                    outboundGray.Bind(TextBlock.TextProperty, new Binding(nameof(Message.DeliveryStatus)) { Converter = _deliveryGlyphConverter });
                    outboundGray.Bind(IsVisibleProperty, new Binding(nameof(Message.DeliveryStatus)) { Converter = _deliveryNotReadConverter });
                    outboundGray.Bind(ToolTip.TipProperty, new Binding(nameof(Message.DeliveryStatus)) { Converter = _deliveryTooltipConverter });

                    var outboundRead = new TextBlock { Text = "\uE73E", FontSize = 11, Foreground = ResolveBrush("App.Accent", Brushes.DeepSkyBlue) };
                    outboundRead.Classes.Add("icon-mdl2");
                    outboundRead.Bind(IsVisibleProperty, new Binding(nameof(Message.DeliveryStatus)) { Converter = _deliveryReadConverter });
                    outboundRead.Bind(ToolTip.TipProperty, new Binding(nameof(Message.DeliveryStatus)) { Converter = _deliveryTooltipConverter });

                    deliveryRow.Children.Add(outboundGray);
                    deliveryRow.Children.Add(outboundRead);
                }
                else
                {
                    var inbound = new TextBlock { Text = "\uE896", FontSize = 11, Opacity = 0.45 };
                    inbound.Classes.Add("icon-mdl2");
                    ToolTip.SetTip(inbound, "Received");
                    deliveryRow.Children.Add(inbound);
                }
                footerRow.Children.Add(deliveryRow);
                Grid.SetColumn(deliveryRow, 1);

                content.Children.Add(senderBlock);
                content.Children.Add(metaRow);
                content.Children.Add(hoverActions);
                content.Children.Add(body);
                content.Children.Add(replyPreview);
                content.Children.Add(footerRow);

                wrapper.Children.Add(avatarBorder);
                wrapper.Children.Add(content);
                Grid.SetColumn(content, 1);

                void ShowActions()
                {
                    hoverActions.Opacity = 1;
                    hoverActions.IsHitTestVisible = true;
                }

                void HideActions()
                {
                    hoverActions.Opacity = 0;
                    hoverActions.IsHitTestVisible = false;
                }

                wrapper.PointerEntered += (_, __) => ShowActions();
                content.PointerEntered += (_, __) => ShowActions();
                avatarBorder.PointerEntered += (_, __) => ShowActions();

                wrapper.PointerExited += (_, __) => HideActions();
                content.PointerExited += (_, __) =>
                {
                    if (!wrapper.IsPointerOver) HideActions();
                };
                avatarBorder.PointerExited += (_, __) =>
                {
                    if (!wrapper.IsPointerOver) HideActions();
                };

                return wrapper;
            }, supportsRecycling: true)
        };
    }

    private Popup BuildComposerEmojiPopup(Control placementTarget)
    {
        var popup = new Popup
        {
            Name = "CompactComposerEmojiPickerPopup",
            PlacementTarget = placementTarget,
            Placement = PlacementMode.Top,
            IsLightDismissEnabled = true
        };
        popup.Opened += (_, __) =>
        {
            try { _vm.ComposerEmojiSearchQuery = string.Empty; } catch { }
        };
        popup.Closed += (_, __) =>
        {
            try { _vm.ComposerEmojiSearchQuery = string.Empty; } catch { }
        };

        var border = new Border
        {
            Background = ResolveBrush("App.Surface", new SolidColorBrush(Color.Parse("#1F2A33"))),
            BorderBrush = ResolveBrush("App.Border", Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8)
        };

        var scroll = new ScrollViewer
        {
            MaxHeight = 320,
            Width = 300,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var stack = new StackPanel { Spacing = 10 };

        var search = new TextBox
        {
            Watermark = "Search emoji or category",
            FontSize = 14,
            MinHeight = 34,
            [!TextBox.TextProperty] = new Binding(nameof(MainWindowViewModel.ComposerEmojiSearchQuery)) { Source = _vm, Mode = BindingMode.TwoWay }
        };

        var selectors = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };

        var categoryCombo = new ComboBox
        {
            FontSize = 13,
            MinHeight = 34,
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(MainWindowViewModel.FilteredComposerEmojiCategories)) { Source = _vm },
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(nameof(MainWindowViewModel.SelectedComposerEmojiCategory)) { Source = _vm, Mode = BindingMode.TwoWay }
        };
        Grid.SetColumn(categoryCombo, 0);

        var toneCombo = new ComboBox
        {
            FontSize = 13,
            MinHeight = 34,
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(MainWindowViewModel.ComposerSkinToneOptions)) { Source = _vm },
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(nameof(MainWindowViewModel.SelectedComposerSkinTone)) { Source = _vm, Mode = BindingMode.TwoWay }
        };
        Grid.SetColumn(toneCombo, 1);

        selectors.Children.Add(categoryCombo);
        selectors.Children.Add(toneCombo);

        var items = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(MainWindowViewModel.VisibleComposerEmojiItems)) { Source = _vm },
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = 48, ItemHeight = 48 }),
            ItemTemplate = new FuncDataTemplate<object>((item, _) =>
            {
                var button = new Button
                {
                    Classes = { "icon-button" },
                    Width = 44,
                    Height = 44,
                    CommandParameter = item?.GetType().GetProperty("Value")?.GetValue(item)?.ToString() ?? string.Empty
                };
                ToolTip.SetTip(button, item?.GetType().GetProperty("Tooltip")?.GetValue(item)?.ToString() ?? string.Empty);
                button.Click += ComposerEmojiPickerItem_Click;

                var emoji = item?.GetType().GetProperty("Value")?.GetValue(item)?.ToString() ?? string.Empty;
                button.Content = new TextBlock
                {
                    Text = emoji,
                    FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                return button;
            }, supportsRecycling: true)
        };

        stack.Children.Add(search);
        stack.Children.Add(selectors);
        stack.Children.Add(items);
        scroll.Content = stack;
        border.Child = scroll;
        popup.Child = border;
        return popup;
    }

    private void ComposerInput_KeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (ComposerEnterPolicy.ShouldSendOnKeyPress(e.Key, e.KeyModifiers))
            {
                if (_vm.SendCommand.CanExecute(null))
                    _vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
        catch { }
    }

    private void CompactConversationWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key != Key.Escape)
                return;

            if (_composerEmojiPopup.IsOpen)
            {
                _composerEmojiPopup.IsOpen = false;
                try { _messageInput?.Focus(); } catch { }
                e.Handled = true;
                return;
            }

            if (_isSearchOpen)
            {
                _isSearchOpen = false;
                _searchHost.IsVisible = false;
                _vm.MessageSearchQuery = string.Empty;
                try { _messageInput?.Focus(); } catch { }
                e.Handled = true;
                return;
            }

            if (_vm.IsPinnedPanelOpen)
            {
                _vm.IsPinnedPanelOpen = false;
                RefreshPinnedHost();
                try { _messageInput?.Focus(); } catch { }
                e.Handled = true;
            }
        }
        catch { }
    }

    private void ComposerEmojiPickerButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            _composerEmojiPopup.IsOpen = !_composerEmojiPopup.IsOpen;
        }
        catch { }
    }

    private void ComposerEmojiPickerItem_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (sender is Button button && button.CommandParameter is string emoji && !string.IsNullOrWhiteSpace(emoji))
            {
                _vm.InsertEmojiIntoMessage(emoji);
                _composerEmojiPopup.IsOpen = false;
                try { _messageInput?.Focus(); } catch { }
            }
        }
        catch { }
    }

    private void CompactDragRegion_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (e.Source is Control sourceControl)
            {
                for (var current = sourceControl; current != null; current = current.Parent as Control)
                {
                    if (current is Button or TextBox or ComboBox or ToggleSwitch or Slider or ScrollBar or RepeatButton)
                        return;
                }
            }

            if (WindowDragHelper.TryBeginMoveDrag(this, e))
            {
                e.Handled = true;
                return;
            }
        }
        catch { }
    }

    private void SearchBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            _isSearchOpen = !_isSearchOpen;
            _searchHost.IsVisible = _isSearchOpen;
            if (_isSearchOpen)
            {
                _searchBox.Focus();
            }
            else
            {
                _vm.MessageSearchQuery = string.Empty;
                try { _messageInput?.Focus(); } catch { }
            }
        }
        catch { }
    }

    private void PinBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_vm.TogglePinnedPanelCommand?.CanExecute(null) == true)
                _vm.TogglePinnedPanelCommand.Execute(null);
            RefreshPinnedHost();
        }
        catch { }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            if (e.PropertyName == nameof(MainWindowViewModel.HasPinnedMessages)
                || e.PropertyName == nameof(MainWindowViewModel.IsPinnedPanelOpen)
                || e.PropertyName == nameof(MainWindowViewModel.PinnedMessages))
            {
                RefreshPinnedHost();
            }
        }
        catch { }
    }

    private void RefreshPinnedHost()
    {
        try
        {
            var shouldShow = _vm.IsPinnedPanelOpen && _vm.HasPinnedMessages;
            _pinnedHost.IsVisible = shouldShow;
            if (!shouldShow)
            {
                _pinnedHost.Child = null;
                return;
            }

            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = "Pinned messages",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = ResolveBrush("App.ForegroundPrimary", Brushes.White)
            });

            foreach (var message in _vm.PinnedMessages.Take(3))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"• {message.RenderedContent}",
                    FontSize = 11,
                    Opacity = 0.82,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 760,
                    Foreground = ResolveBrush("App.ForegroundSecondary", Brushes.LightGray)
                });
            }

            _pinnedHost.Child = stack;
        }
        catch { }
    }

    private void TrySelectTargetContact()
    {
        try
        {
            var target = _vm.Contacts.FirstOrDefault(c =>
                string.Equals(NormalizeUidKey(c.UID ?? string.Empty), _contactUid, StringComparison.OrdinalIgnoreCase));

            if (target != null)
                _vm.SelectedContact = target;
        }
        catch { }
    }

    private void ScrollToLatestMessage()
    {
        try
        {
            if (_vm.Messages.Count <= 0) return;
            _messagesList.ScrollIntoView(_vm.Messages[^1]);
        }
        catch { }
    }

    private string GetLayoutCacheKey()
    {
        return $"CompactConversationWindow:{_contactUid}";
    }

    private void RestoreLayoutFromCache()
    {
        _layoutAutosave.BeginRestore();
        try
        {
            double width = Width;
            double height = Height;
            var position = Position;

            var cached = LayoutCache.Load(GetLayoutCacheKey());
            if (cached is not null)
            {
                if (cached.Width is double cw && cw > 0) width = cw;
                if (cached.Height is double ch && ch > 0) height = ch;
                if (cached.X is double cx && cached.Y is double cy)
                    position = new PixelPoint((int)cx, (int)cy);
                if (cached.State is int cs)
                    WindowState = (WindowState)cs;
            }

            WindowBoundsHelper.EnsureVisible(this, ref width, ref height, ref position);
            Width = width;
            Height = height;
            Position = position;
        }
        catch { }
        finally
        {
            _layoutAutosave.EndRestore();
        }
    }

    private void SaveLayoutToCache()
    {
        try { _layoutAutosave.SaveNow(); } catch { }
    }

    private static string NormalizeUidKey(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
        var trimmed = uid.Trim();
        const string prefix = "usr-";
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..]
            : trimmed;
    }

    private static IBrush ResolveBrush(string key, IBrush fallback)
    {
        try
        {
            if (Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush)
                return brush;
        }
        catch { }

        return fallback;
    }
}

