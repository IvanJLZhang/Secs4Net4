using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Secs4Frmk4.Sml;

namespace TestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var bytes = BitConverter.GetBytes(16711679);
            var byteValue = BitConverter.ToInt32(bytes, 0);

            byte[] value = new byte[] { 0xff, 0xff, 0xfe };
            byte[] value1 = new byte[] { 0xfe, 0xff, 0xff };
            Byte[] result = new byte[4];
            byte[] result1 = new byte[4];
            Array.Copy(value, result, value.Length);
            Array.Copy(value1, result1, value1.Length);

            int INT = BitConverter.ToInt32(result, 0);
            Array.Reverse(result1);
            int result2 = BitConverter.ToInt32(result1, 0);
        }
    }
}
