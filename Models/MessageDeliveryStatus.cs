namespace Zer0Talk.Models
{
    public enum MessageDeliveryStatus
    {
        None = 0,       // Incoming messages or legacy messages with no status
        Pending = 1,    // Created locally, not yet sent over the network
        Sent = 2,       // Successfully transmitted to peer's endpoint
        Delivered = 3   // Peer acknowledged receipt (0xB5 ACK received)
    }
}
