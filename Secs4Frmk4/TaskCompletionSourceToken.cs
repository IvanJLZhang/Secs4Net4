using System.Threading.Tasks;
using Secs4Frmk4.Properties;

namespace Secs4Frmk4
{
    internal class TaskCompletionSourceToken : TaskCompletionSource<SecsMessage>
    {
        internal readonly SecsMessage MessageSent;
        internal readonly int Id;
        internal readonly MessageType MsgType;

        internal TaskCompletionSourceToken(SecsMessage secsMessage, int systemByte, MessageType dataMessage)
        {
            this.MessageSent = secsMessage;
            this.Id = systemByte;
            this.MsgType = dataMessage;
        }

        internal void HandleReplyMessage(SecsMessage replyMsg)
        {
            replyMsg.Name = MessageSent.Name;
            if (replyMsg.F == 0)
            {
                SetException(new SecsException(MessageSent, Resources.SxF0));
                return;
            }

            if (replyMsg.S == 9)
            {
                switch (replyMsg.F)
                {
                    case 1:
                        SetException(new SecsException(MessageSent, Resources.S9F1));
                        break;
                    case 3:
                        SetException(new SecsException(MessageSent, Resources.S9F3));
                        break;
                    case 5:
                        SetException(new SecsException(MessageSent, Resources.S9F5));
                        break;
                    case 7:
                        SetException(new SecsException(MessageSent, Resources.S9F7));
                        break;
                    case 9:
                        SetException(new SecsException(MessageSent, Resources.S9F9));
                        break;
                    case 11:
                        SetException(new SecsException(MessageSent, Resources.S9F11));
                        break;
                    case 13:
                        SetException(new SecsException(MessageSent, Resources.S9F13));
                        break;
                    default:
                        SetException(new SecsException(MessageSent, Resources.S9Fy));
                        break;
                }
                return;
            }

            SetResult(replyMsg);
        }
    }
}