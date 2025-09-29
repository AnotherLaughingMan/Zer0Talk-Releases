using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using ZTalk.Models;
using P2PTalk.ViewModels;

namespace P2PTalk.Utilities
{
    public sealed class EditCountdownConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (values.Count < 2) return string.Empty;
                if (values[0] is not Message m) return string.Empty;
                if (values[1] is not MainWindowViewModel vm) return string.Empty;
                return vm.GetEditRemaining(m);
            }
            catch { return string.Empty; }
        }
    }

    public sealed class EditCountdownVisibleConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (values.Count < 2) return false;
                if (values[0] is not Message m) return false;
                if (values[1] is not MainWindowViewModel vm) return false;
                return vm.ShowEditRemaining(m);
            }
            catch { return false; }
        }
    }

    // Helper for enabling/disabling the Edit button per message
    public sealed class EditEnabledConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (values.Count < 2) return false;
                if (values[0] is not Message m) return false;
                if (values[1] is not MainWindowViewModel vm) return false;
                return vm.CanEditMessage(m);
            }
            catch { return false; }
        }
    }
}
