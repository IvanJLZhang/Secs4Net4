#region 文件说明
/*------------------------------------------------------------------------------
// Copyright © 2018 Granda. All Rights Reserved.
// 苏州广林达电子科技有限公司 版权所有
//------------------------------------------------------------------------------
// File Name: ExtensionHelper
// Author: Ivan JL Zhang    Date: 2018/4/28 14:44:25    Version: 1.0.0
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
using System.Linq;
using System.Text;

namespace Secs4Frmk4
{
    public static class ExtensionHelper
    {

        internal static void Reverse(this byte[] bytes, int begin, int end, int offset)
        {
            if (offset <= 1) return;
            for (int index = 0; index < end; index += offset)
            {
                Array.Reverse(bytes, index, offset);
            }
        }
    }
}
