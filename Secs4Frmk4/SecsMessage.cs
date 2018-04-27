using System;
using System.Collections.Generic;
using Secs4Frmk4.Properties;

namespace Secs4Frmk4
{
    public sealed class SecsMessage
    {
        #region 静态构造方法
        static SecsMessage()
        {
            if (!BitConverter.IsLittleEndian)
            {
                throw new PlatformNotSupportedException("This version is only work on little endian hardware.");
            }
        }
        #endregion

        #region Fields/Properties
        /// <summary>
        /// 重写ToString
        /// </summary>
        /// <returns></returns>
        public override string ToString() => $"{Name ?? String.Empty}:S{S}F{F} {(ReplyExpected ? "W" : string.Empty)}";
        /// <summary>
        /// Function Name
        /// </summary>
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

        internal readonly Lazy<List<ArraySegment<byte>>> RawDatas;

        public IList<ArraySegment<byte>> RawBytes => RawDatas.Value.AsReadOnly();

        private static readonly List<ArraySegment<byte>> EmptyMsgDatas = new List<ArraySegment<byte>>
        {
            new ArraySegment<byte>(new byte[]{0, 0, 0, 10}),// total length: 10
            new ArraySegment<byte>(new byte[]{ })// header
            // item
        };
        #endregion

        #region 构造方法
        /// <summary>
        /// constructor of SecsMessage
        /// </summary>
        /// <param name="s"></param>
        /// <param name="f"></param>
        /// <param name="replyExpected"></param>
        /// <param name="name"></param>
        /// <param name="secsItem"></param>
        public SecsMessage(byte s, byte f, bool replyExpected = true, string name = null, Item secsItem = null)
        {
            if (s > 0b0111_1111)
            {
                throw new ArgumentOutOfRangeException(nameof(s), s, Resources.SecsMessageStreamNumberMustLessThan127);
            }
            S = s;
            F = f;
            ReplyExpected = replyExpected;
            Name = name;
            SecsItem = secsItem;

            RawDatas = new Lazy<List<ArraySegment<byte>>>(() =>
            {
                if (SecsItem == null)
                {
                    return EmptyMsgDatas;
                }

                var result = new List<ArraySegment<byte>>
                {
                    default(ArraySegment<byte>),// total length
                    new ArraySegment<byte>(new byte[]{})// header
                    //item

                };

                var length = 10 + SecsItem.EncodeTo(result);// total length = item + header;

                byte[] msgLengthByte = BitConverter.GetBytes(length);
                Array.Reverse(msgLengthByte);
                result[0] = new ArraySegment<byte>(msgLengthByte);

                return result;
            });
        }
        /// <summary>
        /// constructor of SecsMessage
        /// </summary>
        /// <param name="s"></param>
        /// <param name="f"></param>
        /// <param name="name"></param>
        /// <param name="item"></param>
        public SecsMessage(byte s, byte f, string name, Item item = null) : this(s, f, true, name, item) { }
        #endregion

    }
}