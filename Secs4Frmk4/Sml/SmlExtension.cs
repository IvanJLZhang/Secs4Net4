#region 文件说明
/*------------------------------------------------------------------------------
// Copyright © 2018 Granda. All Rights Reserved.
// 苏州广林达电子科技有限公司 版权所有
//------------------------------------------------------------------------------
// File Name: SmlExtension
// Author: Ivan JL Zhang    Date: 2018/4/27 13:10:46    Version: 1.0.0
// Description: 
//   
// 
// Revision History: 
// <Author>  		<Date>     	 	<Revision>  		<Modification>
// 	
//----------------------------------------------------------------------------*/
#endregion
using System;
using System.Collections.Generic;
using System.IO;
using static Secs4Frmk4.Item;
namespace Secs4Frmk4.Sml
{
    public static class SmlExtension
    {
        #region ToSml
        public static string ToSml(this SecsMessage secsMessage)
        {
            if (secsMessage == null)
                return null;
            using (var sw = new StringWriter())
            {
                secsMessage.WriteTo(sw, 0);
                return sw.ToString();
            }
        }

        private static void WriteTo(this SecsMessage secsMessage, TextWriter textWriter, int indent = 4)
        {
            textWriter.WriteLine(secsMessage.ToString());
            if (secsMessage.SecsItem != null)
            {
                Write(textWriter, secsMessage.SecsItem, indent);
            }
            textWriter.Write(".");
        }

        private static void Write(TextWriter textWriter, Item secsItem, int indent = 4)
        {
            var indentStr = new string(' ', indent);
            textWriter.Write($"{indentStr}<{secsItem.Format.ToSml()} [{secsItem.Count}]");// <A[8] 
            switch (secsItem.Format)
            {
                case SecsFormat.List:
                    textWriter.WriteLine();
                    int indentTemp = indent + 4;
                    for (int index = 0; index < secsItem.Count; index++)
                    {
                        Write(textWriter, secsItem.Items[index], indentTemp);
                    }
                    indent = indentTemp;
                    textWriter.Write(indentStr);
                    break;
                case SecsFormat.ASCII:
                    textWriter.Write($"\'{secsItem.GetString()}\'");// 'content'
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(secsItem.Format), secsItem.Format, "invalid SecsFormat value");
            }
            textWriter.WriteLine('>');
        }

        public static string ToSml(this SecsFormat secsFormat)
        {
            switch (secsFormat)
            {
                case SecsFormat.List:
                    return "L";

                case SecsFormat.ASCII:
                    return "A";

                default:
                    throw new ArgumentOutOfRangeException(nameof(secsFormat), (int)secsFormat, "Invalid enum value");
            }
        }
        #endregion

        #region ToSecsMessage
        public static SecsMessage ToSecsMessage(this string str)
        {
            using (var sr = new StringReader(str))
            {
                return sr.ToSecsMessage();
            }
        }

        private static SecsMessage ToSecsMessage(this TextReader reader)
        {
            var line = reader.ReadLine();
            #region parse First line
            int index = line.IndexOf(':');
            var name = line.Substring(0, index);
            line = line.Substring(index + 1).Trim();
            index = line.IndexOf("S", 0, 1, StringComparison.OrdinalIgnoreCase);
            int indeF = line.IndexOf("F", 0, line.Length, StringComparison.OrdinalIgnoreCase);
            var str = line.Substring(index, indeF - index - 1);
            Byte.TryParse(line.Substring(index + 1, indeF - index - 1), out byte s);

            line = line.Substring(indeF + 1);
            bool replyExpected = false;
            if (line.Contains("W"))
                replyExpected = true;
            var f = replyExpected
               ? byte.Parse(line.Substring(0, line.IndexOf('W')).Trim())
               : byte.Parse(line.Trim());
            #endregion

            Item rootItem = null;
            var stack = new Stack<List<Item>>();
            while ((line = reader.ReadLine()) != null && ParseItem(line, stack, ref rootItem)) { }

            return new SecsMessage(s, f, replyExpected, name, rootItem);
        }

        private static bool ParseItem(string line, Stack<List<Item>> stack, ref Item rootItem)
        {
            line = line.TrimStart();
            if (line[0] == '.')
                return false;

            if (line[0] == '>')
            {
                var itemList = stack.Pop();
                var item = itemList.Count > 0 ? L(itemList) : L();
                if (stack.Count > 0)
                    stack.Peek().Add(item);
                else
                    rootItem = item;
                return true;
            }

            int indexItemL = line.IndexOf('<') + 1;
            int indexSizeL = line.IndexOf('[', indexItemL);

            string format = line.Substring(indexItemL, indexSizeL - indexItemL).Trim();

            if (format == "L")
                stack.Push(new List<Item>());
            else
            {
                int indexSizeR = line.IndexOf(']', indexSizeL);
                int indexItemR = line.IndexOf('>');
                string valueStr = line.Substring(indexSizeR + 1, indexItemR - indexSizeR - 1);
                Item item = Create(format, valueStr);
                if (stack.Count > 0)
                    stack.Peek().Add(item);
                else
                    rootItem = item;
            }

            return true;
        }

        private static readonly Tuple<Func<Item>, Func<string, Item>> AParser = new Tuple<Func<Item>, Func<string, Item>>(A, A);

        private static Item Create(string format, string valueStr)
        {
            switch (format)
            {
                case "A":
                    return ParseStringItem(valueStr, AParser);
                default:
                    throw new SecsException("Unknown SML format :" + format);
            }

            Item ParseStringItem(string str, Tuple<Func<Item>, Func<string, Item>> parser)
            {
                str = str.TrimStart(' ', '\'', '"').TrimEnd(' ', '\'', '"');
                return String.IsNullOrEmpty(str)
                    ? parser.Item1()
                    : parser.Item2(str);
            }
        }
        #endregion
    }
}
