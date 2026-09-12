namespace Outbridge.PyLite.Runtime.Errors
{
    // An immutable node of the script exception name tree.
    public sealed class PyExceptionType
    {
        public string Name { get; }
        public PyExceptionType Parent { get; }   // null only for BaseException

        internal PyExceptionType(string name, PyExceptionType parent)
        {
            Name = name;
            Parent = parent;
        }

        // Iterative walk up Parent (tree depth <= 4); no C# recursion.
        public bool IsSubtypeOf(PyExceptionType other)
        {
            PyExceptionType cur = this;
            while (cur != null)
            {
                if (ReferenceEquals(cur, other))
                    return true;
                cur = cur.Parent;
            }
            return false;
        }

        // UnicodeDecodeError / UnicodeEncodeError carry (encoding, object, start, end, reason) as args.
        public bool IsUnicodeCodecError
        {
            get
            {
                return ReferenceEquals(this, PyExceptionTypes.UnicodeDecodeError)
                    || ReferenceEquals(this, PyExceptionTypes.UnicodeEncodeError);
            }
        }
    }
}
