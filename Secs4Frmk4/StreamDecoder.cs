using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Granda.HSMS
{
    internal sealed class StreamDecoder
    {
        public byte[] Buffer => _buffer;

        public int BufferCount => Buffer.Length - _bufferOffset;

        public int BufferOffset => _bufferOffset;
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
        private Stack<List<Item>> _stack = new Stack<List<Item>>();
        private int _previousRemainedCount;
        private int _decodeStep;
        private readonly byte[] _itemLengthBytes = new byte[4];

        /// <summary>
        /// decoder step
        /// </summary>
        /// <param name="length"></param>
        /// <param name="need"></param>
        /// <returns></returns>
        public delegate int Decoder(ref int length, out int need);

        internal StreamDecoder(int streamBufferSize, Action<MessageHeader> controlMsgHandler, Action<MessageHeader, SecsMessage> dataMsgHandler)
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
        // 0: get total message length 4 bytes
        int GetTotalMessageLength(ref int length, out int need)
        {
            if (!CheckAvailable(ref length, 4, out need))
                return 0;
            Array.Reverse(_buffer, _decodeIndex, 4);
            _messageDataLength = BitConverter.ToUInt32(_buffer, _decodeIndex);
            //Trace.WriteLine($"Get Message Length: {_messageDataLength}");
            _decodeIndex += 4;
            length -= 4;
            return GetMessageHeader(ref length, out need);
        }
        // 1: get message header 10 bytes
        private int GetMessageHeader(ref int length, out int need)
        {
            if (!CheckAvailable(ref length, 10, out need))
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
            if (!CheckAvailable(ref length, 1, out need))
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
            if (!CheckAvailable(ref length, _lengthBits, out need))
                return 3;
            Array.Copy(_buffer, _decodeIndex, _itemLengthBytes, 0, _lengthBits);
            Array.Reverse(_itemLengthBytes, 0, _lengthBits);

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


        private Item BufferdDecodeItem(byte[] bytes, ref int index)
        {
            var format = (SecsFormat)(bytes[index] & 0b1111_1100);
            var lengthBits = (byte)(bytes[index] & 0b0000_0011);
            index++;

            var itemLengthBytes = new byte[4];
            Array.Copy(bytes, index, itemLengthBytes, 0, lengthBits);
            Array.Reverse(itemLengthBytes, 0, lengthBits);

            int dataLength = BitConverter.ToInt32(itemLengthBytes, 0);
            index += lengthBits;

            if (format == SecsFormat.List)
            {
                if (dataLength == 0)
                    return Item.L();
                var list = new List<Item>(dataLength);
                for (int indey = 0; indey < dataLength; indey++)
                {
                    list.Add(BufferdDecodeItem(bytes, ref index));
                }
                return Item.L(list);
            }

            var item = Item.BytesDecode(ref format, bytes, ref index, ref dataLength);
            index += dataLength;
            return item;
        }

        private bool CheckAvailable(ref int length, int required, out int need)
        {
            need = required - length;// 超过了收到的字节长度，报错
            if (need > 0)
                return false;
            need = 0;
            return true;
        }
        #endregion

        internal bool Decode(int length)
        {
            Debug.Assert(length > 0, "decode data length is 0.");

            string byteStr = String.Empty;
            for (int index = 0; index < length; index++)
            {
                byteStr += $"{_buffer[index]:X2} ";
                if ((index + 1) % 10 == 0)
                    byteStr += " ";
                if ((index + 1) % 20 == 0)
                    byteStr += "\r\n";
            }
            Logger.Info(byteStr);
            var decodeLength = length;
            length += _previousRemainedCount;// total available length = current length + previous remained
            int need;
            var nextStep = _decodeStep;
            do
            {
                _decodeStep = nextStep;
                nextStep = _decoders[_decodeStep](ref length, out need);

            } while (nextStep != _decodeStep);
            Debug.Assert(_decodeIndex >= _bufferOffset, "decode index should ahead of buffer index");

            var remainCount = length;
            Debug.Assert(remainCount >= 0, "remain count is only possible grater and equal zero");
            //Trace.WriteLine($"remain data length: {remainCount}");
            Trace.WriteLineIf(_messageDataLength > 0, $"need data count: {need}");

            if (remainCount == 0)
            {
                if (need > Buffer.Length)
                {
                    var newSize = need * 2;
                    Trace.WriteLine($@"<<buffer resizing>>: current size = {_buffer.Length}, new size = {newSize}");

                    // increase buffer size
                    _buffer = new byte[newSize];
                }
                _bufferOffset = 0;
                _decodeIndex = 0;
                _previousRemainedCount = 0;
            }
            else
            {
                _bufferOffset += decodeLength;
                var nextStepReqiredCount = remainCount + need;
                if (nextStepReqiredCount > BufferCount)
                {
                    if (nextStepReqiredCount > Buffer.Length)
                    {
                        var newSize = Math.Max(_messageDataLength / 2, nextStepReqiredCount) * 2;
                        Trace.WriteLine($@"<<buffer resizing>>: current size = {_buffer.Length}, remained = {remainCount}, new size = {newSize}");

                        // out of total buffer size
                        // increase buffer size
                        var newBuffer = new byte[newSize];
                        // keep remained data to new buffer's head
                        Array.Copy(_buffer, _bufferOffset - remainCount, newBuffer, 0, remainCount);
                        _buffer = newBuffer;
                    }

                    else
                    {
                        Trace.WriteLine($@"<<buffer recyling>>: available = {BufferCount}, need = {nextStepReqiredCount}, remained = {remainCount}");

                        // move remained data to buffer's head
                        Array.Copy(_buffer, _bufferOffset - remainCount, _buffer, 0, remainCount);
                    }
                    _bufferOffset = remainCount;
                    _decodeIndex = 0;
                }
                _previousRemainedCount = remainCount;
            }
            return _messageDataLength > 0;
        }

        public void Reset()
        {
            _stack.Clear();
            _decodeStep = 0;
            _decodeIndex = 0;
            _bufferOffset = 0;
            _messageDataLength = 0;
            _previousRemainedCount = 0;
        }
    }
}