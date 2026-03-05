using System;
using System.Globalization;
using System.Collections.Generic;
using Avalonia.Data.Converters;
using Zer0Talk.Models;

namespace Zer0Talk.Utilities
{
    public sealed class ReactionCommandParameterConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is null || values.Count < 2)
            {
                return null;
            }

            if (!TryResolveMessageId(values[0], out var messageId))
            {
                return null;
            }

            var emoji = values[1]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(emoji))
            {
                return null;
            }

            return new ReactionCommandParameter(messageId, emoji.Trim());
        }

        private static bool TryResolveMessageId(object? source, out Guid messageId)
        {
            if (source is Guid guid)
            {
                messageId = guid;
                return true;
            }

            if (source is Message message)
            {
                messageId = message.Id;
                return messageId != Guid.Empty;
            }

            if (source is not null)
            {
                var idProperty = source.GetType().GetProperty("Id");
                if (idProperty is not null)
                {
                    var idValue = idProperty.GetValue(source);
                    if (idValue is Guid reflectedGuid)
                    {
                        messageId = reflectedGuid;
                        return messageId != Guid.Empty;
                    }

                    if (Guid.TryParse(idValue?.ToString(), out var parsedGuid))
                    {
                        messageId = parsedGuid;
                        return messageId != Guid.Empty;
                    }
                }
            }

            if (Guid.TryParse(source?.ToString(), out var textGuid))
            {
                messageId = textGuid;
                return messageId != Guid.Empty;
            }

            messageId = Guid.Empty;
            return false;
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
