using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Secs4Frmk4
{
    internal class ReadOnlyCollection<T> : IReadOnlyCollection<T>
    {
        public int Count => throw new NotImplementedException();

        public IEnumerator<T> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}
