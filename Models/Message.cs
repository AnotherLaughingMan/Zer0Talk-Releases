/*
    Chat message: sender/recipient, timestamps, and encrypted payload references.
*/
using System;
using System.Text.Json.Serialization;

namespace ZTalk.Models
{
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
        public string Content
        {
            get => _content;
            set { if (_content != value) { _content = value; OnPropertyChanged(nameof(Content)); } }
        }

        public DateTime Timestamp { get; set; }
        public DateTime? ReceivedUtc { get; set; }

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
        // Delivery status and timing
        private string _deliveryStatus = string.Empty; // "Pending" | "Sent" | "Received"
        public string DeliveryStatus
        {
            get => _deliveryStatus;
            set { if (_deliveryStatus != value) { _deliveryStatus = value; OnPropertyChanged(nameof(DeliveryStatus)); } }
        }

        public DateTime? DeliveredUtc { get; set; }
        private DateTime? _readUtc;
        public DateTime? ReadUtc
        {
            get => _readUtc;
            set { if (_readUtc != value) { _readUtc = value; OnPropertyChanged(nameof(ReadUtc)); } }
        }
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
    }
}
