// This file contains assembly-level suppressions for specific analyzer warnings where the code is by design.
using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Performance", "CA1859:Use concrete types for improved performance", Justification = "Public API stability; selective fixes applied where internal.")]
