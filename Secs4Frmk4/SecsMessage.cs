namespace Secs4Frmk4
{
    public sealed class SecsMessage
    {
        public string Name { get; internal set; }
        /// <summary>
        /// message stream number
        /// </summary>
        public byte F { get; }
        /// <summary>
        /// message function number
        /// </summary>
        public byte S { get; }

        /// <summary>
        /// expect reply message
        /// </summary>
        public bool ReplyExpected { get; internal set; }

        /// <summary>
        /// the root item of message
        /// </summary>
        public Item SecsItem { get; }

        public int SystenBytes { get; set; }
    }
}