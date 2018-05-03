#region 文件说明
/*------------------------------------------------------------------------------
// Copyright © 2018 Granda. All Rights Reserved.
// 苏州广林达电子科技有限公司 版权所有
//------------------------------------------------------------------------------
// File Name: Logger
// Author: Ivan JL Zhang    Date: 2018/4/25 15:45:59    Version: 1.0.0
// Description: 
//   
// 
// Revision History: 
// <Author>  		<Date>     	 	<Revision>  		<Modification>
// 	
//----------------------------------------------------------------------------*/
#endregion
using System;
using System.Diagnostics;
using Granda.AATS.Log;

namespace Granda.HSMS
{
    internal abstract class Logger
    {
        public static void Info(string message)
        {
            //LogAdapter.WriteLog(new LogRecord(LogLevel.INFO, message, null));
            Trace.WriteLine(message);
        }
        public static void Error(string message, Exception exception = null)
        {
            LogAdapter.WriteLog(new LogRecord(LogLevel.ERROR, message, exception));
            Trace.WriteLine(message + ": " + exception?.Message);
        }
    }
}
