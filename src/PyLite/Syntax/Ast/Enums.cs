namespace Outbridge.PyLite.Syntax.Ast
{
    public enum LiteralKind { Int, Float, Str, Bool, None, Bytes, Ellipsis }

    public enum BinOp
    {
        Add, Sub, Mul, Div, FloorDiv, Mod, Pow,
        LShift, RShift, BitAnd, BitOr, BitXor
    }

    public enum UnaryOp { UAdd, USub, Invert, Not }
    public enum BoolOp { And, Or }
    public enum CompareOp { Lt, Gt, Eq, Ge, Le, Ne, In, NotIn, Is, IsNot }
    public enum CompKind { List, Set, Dict, Generator }
    public enum ArgKind { Positional, Keyword, Star, DoubleStar }
}
