using System;

namespace Zer0Talk.Models
{
    public class LinkPreview
    {
        public string Url { get; set; } = string.Empty;
        public string DisplayUrl { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? SiteName { get; set; }
        public string? ImageUrl { get; set; }
        public string? ImageMimeType { get; set; }
        public byte[]? ImageBytes { get; set; }
        public DateTime FetchedUtc { get; set; } = DateTime.UtcNow;
        public bool HasImage => ImageBytes != null && ImageBytes.Length > 0;
        public bool IsEmpty => string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Description) && !HasImage;
    }
}
