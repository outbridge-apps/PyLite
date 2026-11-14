namespace Outbridge.PyLite.Runtime.Errors
{
    // An immutable node of the script exception name tree.
    public sealed class PyExceptionType
    {
        public string Name { get; }
        public PyExceptionType Parent { get; }   // null only for BaseException
        internal Values.TypeValue ClassValue;    // the class object type(e) answers; set where the class is made
        internal readonly bool IsScriptClass;    // the script chose the name: it never stands for a builtin of that name
        internal PyExceptionType Also;           // a second base, set where the tree is built (decimal.DivisionByZero)
        internal Values.ScriptTypeInfo InstanceTypeInfo;   // an instance's slot table, built on first raise (benign race)

        internal PyExceptionType(string name, PyExceptionType parent, bool isScriptClass = false)
        {
            Name = name;
            Parent = parent;
            IsScriptClass = isScriptClass;
        }

        // Iterative walk up Parent (a script's exception classes may add up to 16 levels); no C# recursion but for a
        // second base, which only the engine's own tree has.
        public bool IsSubtypeOf(PyExceptionType other)
        {
            PyExceptionType cur = this;
            while (cur != null)
            {
                if (ReferenceEquals(cur, other) || (cur.Also != null && cur.Also.IsSubtypeOf(other)))
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
