using System;
using System.Runtime.Serialization;

namespace Secs4Frmk4
{
    internal class SecsException : Exception
    {
        public SecsMessage secsMessage { get; }

        public SecsException(string message) : this(null, message)
        {
        }

        public SecsException(SecsMessage secsMessage, string description) : base(description)
        {
            this.secsMessage = secsMessage;
        }
    }
}