using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Secs4Frmk4
{
    public sealed class Item
    {
        #region Fields/Properties
        /// <summary>
        /// if Format is List RawData is only header bytes.
        /// otherwise include header and value bytes.
        /// </summary>
        private readonly Lazy<byte[]> _rawData;

        private readonly IEnumerable _values;

        public SecsFormat Format { get; private set; }
        /// <summary>
        /// List items
        /// </summary>
        public IList<Item> Items
        {
            get
            {
                return Format != SecsFormat.List
                     ? throw new InvalidOperationException("The item is not a list")
                     : (IList<Item>)_values;
            }
        }
        public int Count
        {
            get
            {
                return Format == SecsFormat.List
                    ? ((IList<Item>)_values).Count
                    : ((string)_values).Length;
            }
        }
        public IList<byte> RawBytes => _rawData.Value;

        private static readonly Encoding Jis8Encoding = Encoding.GetEncoding(50222);
        #endregion

        #region 构造方法
        /// <summary>
        /// List 构造方法
        /// </summary>
        /// <param name="items"></param>
        private Item(IList<Item> items)
        {
            if (items.Count > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(items) + "." + nameof(items.Count), items.Count,
                    @"List items length out of range, max length: 255");

            Format = SecsFormat.List;
            _values = items;
            _rawData = new Lazy<byte[]>(() => new byte[]{
                (byte)SecsFormat.List | 1,
                unchecked((byte)((IList<Item>)(_values)).Count)
            });
        }
        /// <summary>
        /// ASCII, Jis
        /// </summary>
        /// <param name="secsFormat"></param>
        /// <param name="value"></param>
        private Item(SecsFormat secsFormat, string value)
        {
            Format = secsFormat;
            _values = value;
            _rawData = new Lazy<byte[]>(() =>
            {
                var str = (string)_values;
                var bytelength = str.Length;
                var result = EncodeItem(bytelength);
                var encoder = Format == SecsFormat.ASCII ? Encoding.ASCII : Jis8Encoding;
                encoder.GetBytes(str, 0, str.Length, result.Item1, result.Item2);
                return result.Item1;
            });
        }
        /// <summary>
        /// U1, U2, U4, U8
        /// I1, I2, I4, I8
        /// F4, F8
        /// Boolean,
        /// Binary
        /// </summary>
        /// <param name="secsFormat"></param>
        /// <param name="value"></param>
        private Item(SecsFormat secsFormat, Array value)
        {
            Format = secsFormat;
            _values = value;

            _rawData = new Lazy<byte[]>(() =>
            {
                var arr = (Array)_values;
                var byteLength = Buffer.ByteLength(arr);
                var ret = EncodeItem(byteLength);
                var result = ret.Item1;
                var headerLength = ret.Item2;
                Buffer.BlockCopy(arr, 0, result, headerLength, byteLength);
                result.Reverse(headerLength, headerLength + byteLength, byteLength / arr.Length);
                return result;
            });
        }
        /// <summary>
        /// Empty Item(none List)
        /// </summary>
        /// <param name="secsFormat"></param>
        /// <param name="value"></param>
        private Item(SecsFormat secsFormat, IEnumerable value)
        {
            Format = secsFormat;
            _values = value;
            _rawData = new Lazy<byte[]>(() => new byte[] { (byte)((byte)Format | 1), 0 });
        }
        #endregion

        #region helper methods
        /// <summary>
        /// 获取ASCII格式的字串
        /// </summary>
        /// <returns></returns>
        public string GetString()
        {
            return Format != SecsFormat.ASCII && Format != SecsFormat.JIS8
                ? throw new InvalidOperationException("This type is incompatible")
                : (string)_values;
        }
        #endregion

        #region Factory Methods
        public static Item L(IList<Item> items) => items.Count > 0 ? new Item(items) : L();

        public static Item L(IEnumerable<Item> items) => L(items.ToList());

        public static Item L(params Item[] items) => L((IList<Item>)items);

        public static Item A(string value) => value != string.Empty ? new Item(SecsFormat.ASCII, value) : A();

        public static Item B(params byte[] value) => value.Length > 0 ? new Item(SecsFormat.Binary, value) : B();
        public static Item B(IEnumerable<byte> value) => B(value.ToArray());
        #endregion

        #region Share Object
        public static Item L() => EmptyL;
        public static Item A() => EmptyA;
        public static Item B() => EmptyB;
        private static readonly Item EmptyL = new Item(SecsFormat.List, Enumerable.Empty<Item>());
        private static readonly Item EmptyA = new Item(SecsFormat.ASCII, string.Empty);
        private static readonly Item EmptyJ = new Item(SecsFormat.JIS8, string.Empty);
        private static readonly Item EmptyB = new Item(SecsFormat.Binary, Enumerable.Empty<byte>());
        #endregion

        #region internal/private methods
        /// <summary>
        /// Encode item to raw data buffer
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        internal uint EncodeTo(List<ArraySegment<byte>> buffer)
        {
            byte[] bytes = _rawData.Value;
            uint length = unchecked((uint)bytes.Length);
            buffer.Add(new ArraySegment<byte>(bytes));

            if (Format == SecsFormat.List)
            {
                foreach (var item in Items)
                {
                    length += item.EncodeTo(buffer);
                }
            }
            return length;
        }
        /// <summary>
        /// Item的编码规则为Format|content长度所占byte数 + content长度bytes(最多3byte) + content编码
        /// </summary>
        /// <param name="valueCount"></param>
        /// <returns></returns>
        private Tuple<byte[], int> EncodeItem(int valueCount)
        {
            byte[] valueCountBytes = BitConverter.GetBytes(valueCount);
            if (valueCount <= 0xff)
            {// 1 byte
                var result = new byte[valueCount + 2];
                result[0] = (byte)((byte)Format | 1);// Format 加上 长度byte
                result[1] = valueCountBytes[0];
                return new Tuple<byte[], int>(result, 2);
            }
            if (valueCount <= 0xffff)
            {// 2 byte
                var result = new byte[valueCount + 3];
                result[0] = (byte)((byte)Format | 2);// Format 加上 长度
                result[1] = valueCountBytes[1];
                result[2] = valueCountBytes[0];
                return new Tuple<byte[], int>(result, 3);
            }
            if (valueCount <= 0xffffff)
            {// 3 byte
                var result = new byte[valueCount + 4];
                result[0] = (byte)((byte)Format | 3);// Format 加上 长度
                result[1] = valueCountBytes[2];
                result[2] = valueCountBytes[1];
                result[3] = valueCountBytes[0];
                return new Tuple<byte[], int>(result, 4);
            }

            throw new ArgumentOutOfRangeException(nameof(valueCount), valueCount, $@"Item data length:{valueCount} is overflow");
        }

        internal static Item BytesDecode(ref SecsFormat secsFormat, byte[] data, ref int index, ref int length)
        {
            switch (secsFormat)
            {
                case SecsFormat.ASCII:
                    return length == 0 ? A() : A(Encoding.ASCII.GetString(data, index, length));
                default:
                    throw new ArgumentException(@"Invalid format", nameof(secsFormat));
            }
        }

        internal T[] GetValues<T>() where T : struct
        {
            if (Format == SecsFormat.List)
                throw new InvalidOperationException("The item is list.");
            if (Format == SecsFormat.ASCII || Format == SecsFormat.JIS8)
                throw new InvalidOperationException("The item is a string");

            if (_values is T[] arr)
                return arr;

            throw new InvalidOperationException("The type is incompatible");
        }
        #endregion

    }
}