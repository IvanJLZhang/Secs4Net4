using System;

namespace Secs4Frmk4
{
    public struct MessageHeader
    {
        public byte S { get; internal set; }
        public byte F { get; internal set; }
        public bool ReplyExpected { get; internal set; }
        public ushort DeviceId { get; internal set; }
        public int SystemBytes { get; internal set; }
        public MessageType MessageType { get; internal set; }

        internal byte[] EncodeTo(byte[] buffer)
        {
            // Device Id
            var values = BitConverter.GetBytes(DeviceId);
            buffer[0] = values[1];
            buffer[1] = values[0];

            // S, ReplyExpected
            buffer[2] = (byte)(S | (ReplyExpected ? 0b1000_0000 : 0));

            // F
            buffer[3] = F;

            // PType: 0=> SECS-II Encoding
            buffer[4] = 0;

            // MessageType
            buffer[5] = (byte)MessageType;

            values = BitConverter.GetBytes(SystemBytes);
            buffer[6] = values[3];
            buffer[7] = values[2];
            buffer[8] = values[1];
            buffer[9] = values[0];

            return buffer;

        }

        internal static MessageHeader Decode(byte[] buffer, int startIndex)
        {
            ushort deviceId = unchecked(BitConverter.ToUInt16(new byte[] {
                buffer[startIndex + 1],
                buffer[startIndex],
            }, 0));

            int systemBytes = unchecked(BitConverter.ToInt32(new byte[] {
                buffer[startIndex + 9],
                buffer[startIndex + 8],
                buffer[startIndex + 7],
                buffer[startIndex + 6],
            }, 0));

            return new MessageHeader
            {
                DeviceId = deviceId,
                ReplyExpected = (buffer[startIndex + 2] & 0b1000_0000) != 0,
                S = (byte)(buffer[startIndex + 2] & 0b0111_111),
                F = buffer[startIndex + 3],
                MessageType = (MessageType)buffer[startIndex + 5],
                SystemBytes = systemBytes,
            };
        }
    }
}