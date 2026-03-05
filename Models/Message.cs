/*
    Chat message: sender/recipient, timestamps, and encrypted payload references.
*/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using Zer0Talk.Utilities;

namespace Zer0Talk.Models
{
    public sealed class MessageReactionAggregate
    {
        public string Emoji { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class Message : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public Guid Id { get; set; }

        private string _senderUID = string.Empty;
        public string SenderUID
        {
            get => _senderUID;
            set { if (_senderUID != value) { _senderUID = value; OnPropertyChanged(nameof(SenderUID)); } }
        }

        private string _recipientUID = string.Empty;
        public string RecipientUID
        {
            get => _recipientUID;
            set { if (_recipientUID != value) { _recipientUID = value; OnPropertyChanged(nameof(RecipientUID)); } }
        }

        private string _content = string.Empty;
        private string _renderedContent = string.Empty;
        public string Content
        {
            get => _content;
            set
            {
                var normalized = value ?? string.Empty;
                if (_content != normalized)
                {
                    _content = normalized;
                    OnPropertyChanged(nameof(Content));
                    RenderedContent = MarkdownCodeBlockLanguageAnnotator.Annotate(_content);
                }
            }
        }

        [JsonIgnore]
        public string RenderedContent
        {
            get => _renderedContent;
            private set
            {
                if (_renderedContent != value)
                {
                    _renderedContent = value;
                    OnPropertyChanged(nameof(RenderedContent));
                }
            }
        }

        public DateTime Timestamp { get; set; }
        public DateTime? ReceivedUtc { get; set; }

        private MessageDeliveryStatus _deliveryStatus;
        public MessageDeliveryStatus DeliveryStatus
        {
            get => _deliveryStatus;
            set { if (_deliveryStatus != value) { _deliveryStatus = value; OnPropertyChanged(nameof(DeliveryStatus)); } }
        }

        // Optional reply metadata for inline reply chips.
        public Guid? ReplyToMessageId { get; set; }

        private string? _replyToPreview;
        public string? ReplyToPreview
        {
            get => _replyToPreview;
            set { if (_replyToPreview != value) { _replyToPreview = value; OnPropertyChanged(nameof(ReplyToPreview)); OnPropertyChanged(nameof(HasReplyMetadata)); } }
        }

        [JsonIgnore]
        public bool HasReplyMetadata => ReplyToMessageId.HasValue && !string.IsNullOrWhiteSpace(ReplyToPreview);

        private bool _isEdited;
        public bool IsEdited
        {
            get => _isEdited;
            set { if (_isEdited != value) { _isEdited = value; OnPropertyChanged(nameof(IsEdited)); } }
        }

        private DateTime? _editedUtc;
        public DateTime? EditedUtc
        {
            get => _editedUtc;
            set { if (_editedUtc != value) { _editedUtc = value; OnPropertyChanged(nameof(EditedUtc)); } }
        }

        public byte[] Signature { get; set; } = Array.Empty<byte>(); // Ed25519 signature over (SenderUID|RecipientUID|Timestamp|Content)
        public byte[] SenderPublicKey { get; set; } = Array.Empty<byte>(); // included for verification and caching
        
        // Pairing for simulated loopback: links outbound and echo for delivery state mirroring
        public Guid? RelatedMessageId { get; set; }
        // Additional message properties
        // TODO: Markdown and URL rendering support (scaffold later)
        private LinkPreview? _linkPreview;
        public LinkPreview? LinkPreview
        {
            get => _linkPreview;
            set
            {
                if (!ReferenceEquals(_linkPreview, value))
                {
                    _linkPreview = value;
                    OnPropertyChanged(nameof(LinkPreview));
                    OnPropertyChanged(nameof(HasLinkPreview));
                }
            }
        }

        [JsonIgnore]
        public bool HasLinkPreview => _linkPreview != null && !_linkPreview.IsEmpty;

        [JsonIgnore]
        private bool _isSearchMatch;
        [JsonIgnore]
        public bool IsSearchMatch
        {
            get => _isSearchMatch;
            set { if (_isSearchMatch != value) { _isSearchMatch = value; OnPropertyChanged(nameof(IsSearchMatch)); } }
        }

        [JsonIgnore]
        private bool _isActiveSearchMatch;
        [JsonIgnore]
        public bool IsActiveSearchMatch
        {
            get => _isActiveSearchMatch;
            set { if (_isActiveSearchMatch != value) { _isActiveSearchMatch = value; OnPropertyChanged(nameof(IsActiveSearchMatch)); } }
        }

        private bool _isPinned;
        public bool IsPinned
        {
            get => _isPinned;
            set { if (_isPinned != value) { _isPinned = value; OnPropertyChanged(nameof(IsPinned)); } }
        }

        private bool _isStarred;
        public bool IsStarred
        {
            get => _isStarred;
            set { if (_isStarred != value) { _isStarred = value; OnPropertyChanged(nameof(IsStarred)); } }
        }

        private bool _isImportant;
        public bool IsImportant
        {
            get => _isImportant;
            set { if (_isImportant != value) { _isImportant = value; OnPropertyChanged(nameof(IsImportant)); } }
        }

        [JsonIgnore]
        public bool HasAttachmentLikeContent
        {
            get
            {
                if (HasLinkPreview) return true;
                var text = Content ?? string.Empty;
                return text.IndexOf("http://", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("https://", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        [JsonIgnore]
        public bool IsEmojiOnly
        {
            get => IsContentEmojiOnly(Content);
        }

        /// <summary>
        /// Detects if the content string contains only emojis and whitespace.
        /// Used to render emoji-only messages at full size.
        /// </summary>
        private static bool IsContentEmojiOnly(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;

            var text = content.Trim();
            if (string.IsNullOrEmpty(text)) return false;

            int emojiCount = 0;
            int i = 0;

            while (i < text.Length)
            {
                var c = text[i];

                // Skip whitespace
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                // Check for emoji via codepoint ranges (simplified but effective)
                // Emoji ranges: 0x1F300-0x1F9FF (misc symbols, emoticons, etc.)
                int codepoint = char.ConvertToUtf32(text, i);
                if ((codepoint >= 0x1F300 && codepoint <= 0x1F9FF) ||  // Emoticons, symbols
                    (codepoint >= 0x2600 && codepoint <= 0x27BF) ||   // Miscellaneous Symbols
                    (codepoint >= 0x1F000 && codepoint <= 0x1F02F) ||  // Mahjong, Domino
                    (codepoint >= 0x1F0A0 && codepoint <= 0x1F0FF))   // Playing cards
                {
                    emojiCount++;
                    // Handle surrogate pairs (emojis are often 2 char units)
                    if (char.IsHighSurrogate(text[i]))
                    {
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }
                else
                {
                    // Found non-emoji, non-whitespace character
                    return false;
                }
            }

            return emojiCount > 0;
        }

        private Dictionary<string, List<string>> _reactions = new();
        public Dictionary<string, List<string>> Reactions
        {
            get => _reactions;
            set
            {
                _reactions = value ?? new Dictionary<string, List<string>>();
                RebuildReactionAggregates();
                OnPropertyChanged(nameof(Reactions));
                OnPropertyChanged(nameof(HasReactions));
            }
        }

        [JsonIgnore]
        public ObservableCollection<MessageReactionAggregate> ReactionAggregates { get; } = new();

        [JsonIgnore]
        public bool HasReactions => ReactionAggregates.Count > 0;

        [JsonIgnore]
        public IReadOnlyList<ReactionEmojiCategory> ReactionEmojiCategories => ReactionEmojiCatalogLoader.LoadCategories();

        public void EnsureReactionStateLoaded()
        {
            RebuildReactionAggregates();
        }

        public bool HasReaction(string actorUid, string emoji)
        {
            var normalizedActor = NormalizeActorUid(actorUid);
            var normalizedEmoji = NormalizeEmoji(emoji);
            if (string.IsNullOrWhiteSpace(normalizedActor) || string.IsNullOrWhiteSpace(normalizedEmoji)) return false;
            if (!Reactions.TryGetValue(normalizedEmoji, out var actors) || actors == null) return false;
            return actors.Any(a => string.Equals(NormalizeActorUid(a), normalizedActor, StringComparison.OrdinalIgnoreCase));
        }

        public void ApplyReaction(string actorUid, string emoji, bool isAdd)
        {
            var normalizedActor = NormalizeActorUid(actorUid);
            var normalizedEmoji = NormalizeEmoji(emoji);
            if (string.IsNullOrWhiteSpace(normalizedActor) || string.IsNullOrWhiteSpace(normalizedEmoji)) return;

            if (!Reactions.TryGetValue(normalizedEmoji, out var actors) || actors == null)
            {
                actors = new List<string>();
                Reactions[normalizedEmoji] = actors;
            }

            var index = actors.FindIndex(a => string.Equals(NormalizeActorUid(a), normalizedActor, StringComparison.OrdinalIgnoreCase));
            if (isAdd)
            {
                if (index < 0) actors.Add(normalizedActor);
            }
            else if (index >= 0)
            {
                actors.RemoveAt(index);
            }

            if (actors.Count == 0)
            {
                Reactions.Remove(normalizedEmoji);
            }

            RebuildReactionAggregates();
            OnPropertyChanged(nameof(Reactions));
            OnPropertyChanged(nameof(HasReactions));
        }

        private void RebuildReactionAggregates()
        {
            ReactionAggregates.Clear();
            foreach (var entry in Reactions
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null)
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                var count = entry.Value
                    .Select(NormalizeActorUid)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                if (count <= 0) continue;
                ReactionAggregates.Add(new MessageReactionAggregate { Emoji = entry.Key, Count = count });
            }
            OnPropertyChanged(nameof(ReactionAggregates));
        }

        private static string NormalizeActorUid(string actorUid)
        {
            var value = (actorUid ?? string.Empty).Trim();
            if (value.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) && value.Length > 4)
            {
                value = value.Substring(4);
            }
            return value;
        }

        private static string NormalizeEmoji(string emoji)
        {
            return (emoji ?? string.Empty).Trim();
        }
    }
}
