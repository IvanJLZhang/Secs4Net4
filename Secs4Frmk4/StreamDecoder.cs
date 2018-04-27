using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Secs4Frmk4
{
    internal sealed class StreamDecoder
    {
        private byte[] _buffer;
        private int _bufferOffset;
        private int _decodeIndex;
        private Action<MessageHeader, SecsMessage> _dataMsgHandler;
        private Action<MessageHeader> _controlMsgHandler;
        private Decoder[] _decoders;
        private uint _messageDataLength;
        private MessageHeader _messageHeader;
        private SecsFormat _format;
        private byte _lengthBits;
        private int _itemLength;
        private Stack<List<Item>> _stack;
        private readonly byte[] _itemLengthBytes = new byte[4];

        /// <summary>
        /// decoder step
        /// </summary>
        /// <param name="length"></param>
        /// <param name="need"></param>
        /// <returns></returns>
        public delegate int Decoder(ref int length, out int need);

        public StreamDecoder(int streamBufferSize, Action<MessageHeader> controlMsgHandler, Action<MessageHeader, SecsMessage> dataMsgHandler)
        {
            _buffer = new byte[streamBufferSize];
            _bufferOffset = 0;
            _decodeIndex = 0;
            _dataMsgHandler = dataMsgHandler;
            _controlMsgHandler = controlMsgHandler;

            _decoders = new Decoder[]
            {
                GetTotalMessageLength,
                GetMessageHeader,
                GetItemHeader,
                GetItemLength,
                GetItem,
            };
        }

        #region Decoders
        public byte[] Buffer => _buffer;
        // 0: get total message length 4 bytes
        int GetTotalMessageLength(ref int length, out int need)
        {
            if (!CheckAvailable(ref length, 4, out need))
                return 0;
            Array.Reverse(_buffer, _decodeIndex, 4);
            _messageDataLength = BitConverter.ToUInt32(_buffer, _decodeIndex);
            Trace.WriteLine($"Get Message Length: {_messageDataLength}");
            _decodeIndex += 4;
            return GetMessageHeader(ref length, out need);
        }
        // 1: get message header 10 bytes
        private int GetMessageHeader(ref int length, out int need)
        {
            if (CheckAvailable(ref length, 10, out need))
                return 1;
            _messageHeader = MessageHeader.Decode(_buffer, _decodeIndex);
            _decodeIndex += 10;
            _messageDataLength -= 10;
            length -= 10;
            if (_messageDataLength == 0)
            {
                if (_messageHeader.MessageType == MessageType.DataMessage)
                    _dataMsgHandler(_messageHeader, new SecsMessage(_messageHeader.S, _messageHeader.F, _messageHeader.ReplyExpected, string.Empty));
                else
                    _controlMsgHandler(_messageHeader);
                return 0;
            }

            if (length >= _messageDataLength)
            {
                Trace.WriteLine("Get Complete Data Message with total data");
                _dataMsgHandler(_messageHeader, new SecsMessage(_messageHeader.S,
                    _messageHeader.F,
                    _messageHeader.ReplyExpected,
                    string.Empty,
                    BufferdDecodeItem(_buffer, ref _decodeIndex)));

                length -= (int)_messageDataLength;
                _messageDataLength = 0;
                return 0;// complete with message received
            }

            return GetItemHeader(ref length, out need);
        }
        // 2: get _format + lengtnBits(2bit) 1 byte
        private int GetItemHeader(ref int length, out int need)
        {
            if (CheckAvailable(ref length, 1, out need))
                return 2;
            _format = (SecsFormat)(_buffer[_decodeIndex] & 0b1111_1100);
            _lengthBits = (byte)(_buffer[_decodeIndex] & 0b0000_0011);
            _decodeIndex++;
            _messageDataLength--;
            length--;
            return GetItemLength(ref length, out need);
        }
        // 3: get _itemLength _lengthBits bytes, at most 3 byte
        private int GetItemLength(ref int length, out int need)
        {
            if (CheckAvailable(ref length, 1, out need))
                return 3;
            Array.Copy(_buffer, _decodeIndex, _itemLengthBytes, 0, _lengthBits);
            Array.Reverse(_itemLengthBytes, 0, 4);

            _itemLength = BitConverter.ToInt32(_itemLengthBytes, 0);
            Array.Clear(_itemLengthBytes, 0, 4);
            Trace.WriteLineIf(_format != SecsFormat.List, $"Get format: {_format}, length: {_itemLength}");

            _decodeIndex += _lengthBits;
            _messageDataLength -= _lengthBits;
            length -= _lengthBits;
            return GetItem(ref length, out need);
        }
        // 4: get item value
        private int GetItem(ref int length, out int need)
        {
            need = 0;
            Item item;
            if (_format == SecsFormat.List)
            {
                if (_itemLength == 0)
                {
                    item = Item.L();
                }
                else
                {
                    _stack.Push(new List<Item>(_itemLength));
                    return GetItemHeader(ref length, out need);
                }
            }
            else
            {
                if (!CheckAvailable(ref length, _itemLength, out need))
                    return 4;
                item = Item.BytesDecode(ref _format, _buffer, ref _decodeIndex, ref _itemLength);
                Trace.WriteLine($"Complete Item: {_format}");

                _decodeIndex += _itemLength;
                _messageDataLength -= (uint)_itemLength;
                length -= _itemLength;
            }

            if (_stack.Count == 0)
            {
                Trace.WriteLine("Get Complete Data Message by stream decoded");
                _dataMsgHandler(_messageHeader, new SecsMessage(_messageHeader.S, _messageHeader.F, _messageHeader.ReplyExpected, string.Empty, item));
                return 0;
            }

            var list = _stack.Peek();
            list.Add(item);

            while (list.Count == list.Capacity)
            {
                item = Item.L(_stack.Pop());
                Trace.WriteLine($"Complete List: {item.Count}");
                if (_stack.Count > 0)
                {
                    list = _stack.Peek();
                    list.Add(item);
                }
                else
                {
                    Trace.WriteLine("Get Complete Data Message by stream decoded");
                    _dataMsgHandler(_messageHeader, new SecsMessage(_messageHeader.S, _messageHeader.F, _messageHeader.ReplyExpected, string.Empty, item));
                    return 0;
                }
            }

            return GetItemHeader(ref length, out need);
        }


        private Item BufferdDecodeItem(byte[] buffer, ref int decodeIndex)
        {
            throw new NotImplementedException();
        }

        private bool CheckAvailable(ref int length, int required, out int need)
        {
            need = required - length;
            if (need > 0)
                return false;
            need = 0;
            return true;
        }
        #endregion

        internal bool Decode(int length)
        {
            Debug.Assert(length > 0, "decode data length is 0.");
            return false;
        }
    }
}