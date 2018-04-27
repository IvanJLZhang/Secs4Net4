using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Secs4Frmk4
{
    public interface IReadOnlyCollection<out T>: IEnumerable<T>, IEnumerable
    {
        int Count { get; }
    }
}
