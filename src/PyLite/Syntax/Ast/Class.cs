using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax.Ast
{
    // record class. Body is restricted: def methods + class-body field annotations (for
    // @dataclass) + pass. Single inheritance only.
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

    public sealed class ClassDefNode : StmtNode
    {
        public readonly string Name;
        public readonly ExprNode Base;                        // null => no base
        public readonly IReadOnlyList<FuncDefNode> Methods;
        public readonly IReadOnlyList<ClassField> Fields;     // class-body annotations, declaration order
        public readonly IReadOnlyList<ExprNode> Decorators;   // source order (outermost first); applied bottom-up
        public ClassDefNode(SourceInfo src, string name, ExprNode baseCls, IReadOnlyList<FuncDefNode> methods,
            IReadOnlyList<ClassField> fields, IReadOnlyList<ExprNode> decorators) : base(src, NodeKind.ClassDef)
        {
            Name = name; Base = baseCls; Methods = methods; Fields = fields; Decorators = decorators;
        }
    }
}
