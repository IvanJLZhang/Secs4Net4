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
            string sml = @"ECR:S1F13 W
  <L [2] 
    <A [4] 'MDLN'>
    <A [7] 'SOFTREV'>
  >
.";
           var message = sml.ToSecsMessage();

            var smll = message.ToSml();

            message = smll.ToSecsMessage();
        }
    }
}
