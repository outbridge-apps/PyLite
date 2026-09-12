namespace Outbridge.PyLite.Runtime.Values
{
    // Closed value tag; engines dispatch on this, never on C# `is` cascades.
    internal enum ValueKind
    {
        None, Bool, Int, Float, Str, Bytes,
        Tuple, List, Dict, Set, FrozenSet,
        Range, Slice, DictView, Iterator,
        Function, BoundMethod, BuiltinFunction, Module, Type,
        Exception, Opaque, RecordClass, RecordInstance
    }

    public enum PyBinOp { Add, Sub, Mul, TrueDiv, FloorDiv, Mod, Pow, LShift, RShift, BitAnd, BitOr, BitXor }
    public enum PyUnaryOp { Neg, Pos, Invert }
    public enum PyCmpOp { Eq, Ne, Lt, Le, Gt, Ge }
}
