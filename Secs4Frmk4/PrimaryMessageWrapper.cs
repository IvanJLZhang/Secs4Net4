using System;

namespace Secs4Frmk4
{
    public class PrimaryMessageWrapper : EventArgs
    {
        private SecsGem secsGem;
        private MessageHeader header;
        public SecsMessage Message { get; }
        public int MessageId => header.SystemBytes;
        public PrimaryMessageWrapper(SecsGem secsGem, MessageHeader header, SecsMessage secsMessage)
        {
            this.secsGem = secsGem;
            this.header = header;
            this.Message = secsMessage;
        }
        public override string ToString()
        {
            return Message.ToString();
        }
    }
}