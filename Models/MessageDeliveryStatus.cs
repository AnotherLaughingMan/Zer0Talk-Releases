namespace Zer0Talk.Models
{
    public enum MessageDeliveryStatus
    {
        None = 0,       // Incoming messages or legacy messages with no status
        Pending = 1,    // Created locally, not yet sent over the network
        Sent = 2,       // Successfully transmitted to peer's device
        Delivered = 3,  // Peer device acknowledged receipt (0xB5 ACK received)
        Read = 4        // Peer has opened and read the message (0xB7 read receipt received)
    }
}
