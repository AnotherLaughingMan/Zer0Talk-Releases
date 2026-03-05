using System;
using Zer0Talk.Models;

namespace Zer0Talk.Utilities
{
    public static class MessageSpaceFilter
    {
        public static bool Matches(Message message, int viewIndex, bool isUnread)
        {
            if (message == null)
            {
                return false;
            }

            var normalized = Math.Clamp(viewIndex, 0, 3);
            return normalized switch
            {
                1 => isUnread,
                2 => message.IsPinned || message.IsStarred || message.IsImportant,
                3 => message.HasAttachmentLikeContent,
                _ => true,
            };
        }
    }
}