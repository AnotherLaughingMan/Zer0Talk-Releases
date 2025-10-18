namespace ZTalk.Models;

/// <summary>
/// Represents the type/severity of a notification
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// Informational message
    /// </summary>
    Information,
    
    /// <summary>
    /// Warning message
    /// </summary>
    Warning,
    
    /// <summary>
    /// Error message
    /// </summary>
    Error,
    
    /// <summary>
    /// Success message
    /// </summary>
    Success
}
