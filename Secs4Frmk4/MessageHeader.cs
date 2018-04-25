namespace Secs4Frmk4
{
    internal class MessageHeader
    {
        public MessageHeader()
        {
        }

        public byte S { get; internal set; }
        public byte F { get; internal set; }
        public bool ReplyExpected { get; internal set; }
        public ushort DeviceId { get; internal set; }
        public int SystemBytes { get; internal set; }
    }
}