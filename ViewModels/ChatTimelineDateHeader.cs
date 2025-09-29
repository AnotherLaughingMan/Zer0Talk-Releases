using System;
using System.Globalization;

namespace P2PTalk.ViewModels
{
    public sealed class ChatTimelineDateHeader
    {
        public ChatTimelineDateHeader(DateTime date)
        {
            Date = date.Date;
        }

        public DateTime Date { get; }

        public string Label => Date.ToString("ddd, MMM d, yyyy", CultureInfo.CurrentCulture);
    }
}
