namespace Zer0Talk.Utilities;

public enum OverlayEscapeAction
{
    None = 0,
    CloseFullProfile,
    CloseSettingsOverlay,
    CloseNotificationDrop,
    CloseChatSearch,
    ClosePinnedDrop
}

public static class OverlayEscapePolicy
{
    public static OverlayEscapeAction Resolve(
        bool isFullProfileOpen,
        bool isSettingsOverlayVisible,
        bool isNotificationDropOpen,
        bool isChatSearchOpen,
        bool isPinnedDropOpen)
    {
        if (isFullProfileOpen)
            return OverlayEscapeAction.CloseFullProfile;

        if (isSettingsOverlayVisible)
            return OverlayEscapeAction.CloseSettingsOverlay;

        if (isNotificationDropOpen)
            return OverlayEscapeAction.CloseNotificationDrop;

        if (isChatSearchOpen)
            return OverlayEscapeAction.CloseChatSearch;

        if (isPinnedDropOpen)
            return OverlayEscapeAction.ClosePinnedDrop;

        return OverlayEscapeAction.None;
    }
}
