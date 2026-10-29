using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax.Ast
{
    // record class. Body is restricted: def methods + class-body field annotations (for
    // @dataclass) + constants + docstrings + pass. Single inheritance only.
    public sealed class ClassField : Node
    {
        public readonly string Name;
        public readonly ExprNode Default;   // null => no default
        public readonly string AnnotationName;   // the annotation when it is a plain name, else null
        public readonly string AnnotationHead;   // that name, or the base name of Name[...] - "ClassVar", "InitVar"
        public ClassField(SourceInfo src, string name, ExprNode def, string annotationName, string annotationHead = null) : base(src)
        {
            Name = name; Default = def; AnnotationName = annotationName; AnnotationHead = annotationHead ?? annotationName;
        }
    }

    // NAME = expr in a class body (1.1.0). The parser assigns expr to a hidden variable just before the
    // class statement and Value reads it back, so the class body needs no scope of its own.
    public sealed class ClassConstant : Node
    {
        public readonly string Name;
        public readonly ExprNode Value;
        public ClassConstant(SourceInfo src, string name, ExprNode value) : base(src) { Name = name; Value = value; }
    }

    public sealed class ClassDefNode : StmtNode
    {
        public readonly string Name;
        public readonly ExprNode Base;                        // null => no base
        public readonly IReadOnlyList<FuncDefNode> Methods;
        public readonly IReadOnlyList<ClassField> Fields;     // class-body annotations, declaration order
        public readonly IReadOnlyList<ExprNode> Decorators;   // source order (outermost first); applied bottom-up
        public readonly IReadOnlyList<ClassConstant> Constants;   // class-body assignments, in order
        public readonly string CellName;   // hidden variable bound to the class when a method calls super(); else null
        public readonly string Mixin;      // "str" or "int" in class X(str, Enum); else null
        public ClassDefNode(SourceInfo src, string name, ExprNode baseCls, IReadOnlyList<FuncDefNode> methods,
            IReadOnlyList<ClassField> fields, IReadOnlyList<ExprNode> decorators, IReadOnlyList<ClassConstant> constants = null,
            string cellName = null, string mixin = null)
            : base(src, NodeKind.ClassDef)
        {
            Name = name; Base = baseCls; Methods = methods; Fields = fields; Decorators = decorators;
            Constants = constants ?? AstList.Empty<ClassConstant>();
            CellName = cellName;
            Mixin = mixin;
        }
    }
}
