using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        private void ValidateAssignTarget(ExprNode root) => ValidateTargets(root, false);

        private void ValidateDelTarget(ExprNode root) => ValidateTargets(root, true);

        // CPython's parser first (_PyPegen_get_invalid_target: the first invalid target in source order), then its
        // compiler (starred elements, __debug__); no C# recursion over user data (R-PAR-07).
        private void ValidateTargets(ExprNode root, bool delete)
        {
            var stack = new Stack<ExprNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ExprNode t = stack.Pop();
                if (t is NameNode || t is AttributeNode || t is IndexNode)
                    continue;
                IReadOnlyList<ExprNode> elts = Elements(t);
                if (elts != null)
                {
                    for (int i = elts.Count - 1; i >= 0; i--)
                        stack.Push(elts[i]);
                    continue;
                }
                StarredNode s = t as StarredNode;
                if (s != null && !delete)
                {
                    stack.Push(s.Value);
                    continue;
                }
                throw ErrorAtSrc(t.Src, (delete ? "cannot delete " : "cannot assign to ") + ExprName(t));
            }

            stack.Push(root);
            while (stack.Count > 0)
            {
                ExprNode t = stack.Pop();
                NameNode name = t as NameNode;
                if (name != null)
                    ForbidDebug(name.Id, t.Src, delete);
                AttributeNode attr = t as AttributeNode;
                if (attr != null)
                    ForbidDebug(attr.Attr, t.Src, delete);
                IReadOnlyList<ExprNode> elts = Elements(t);
                if (elts != null)
                {
                    int starred = 0;
                    foreach (ExprNode elt in elts)
                    {
                        if (elt is StarredNode)
                            starred++;
                    }
                    if (starred > 1)
                        throw ErrorAtSrc(t.Src, "multiple starred expressions in assignment");
                    for (int i = elts.Count - 1; i >= 0; i--)
                        stack.Push(elts[i]);
                    continue;
                }
                StarredNode s = t as StarredNode;
                if (s != null)
                {
                    if (t == root)
                        throw ErrorAtSrc(t.Src, "starred assignment target must be in a list or tuple");
                    stack.Push(s.Value);
                }
            }
        }

        private static IReadOnlyList<ExprNode> Elements(ExprNode t)
        {
            TupleNode tn = t as TupleNode;
            if (tn != null)
                return tn.Elts;
            ListNode ln = t as ListNode;
            return ln != null ? ln.Elts : null;
        }

        // CPython's forbidden_name: __debug__ is bound nowhere (a target, parameter, alias, def or class name, keyword
        // argument, capture), reported where CPython reports it.
        private void ForbidDebug(string name, SourceInfo src, bool delete = false)
        {
            if (name == "__debug__")
                throw ErrorAtSrc(src, (delete ? "cannot delete " : "cannot assign to ") + "__debug__");
        }

        private void ValidateAugTarget(ExprNode lhs)
        {
            NameNode name = lhs as NameNode;
            AttributeNode attr = lhs as AttributeNode;
            if (name != null || attr != null)
                ForbidDebug(name != null ? name.Id : attr.Attr, lhs.Src);
            if (lhs is NameNode || lhs is AttributeNode || lhs is IndexNode)
                return;
            throw ErrorAtSrc(lhs.Src, "'" + ExprName(lhs) + "' is an illegal expression for augmented assignment");
        }

        // _PyPegen_get_expr_name
        private static string ExprName(ExprNode t)
        {
            switch (t)
            {
                case LiteralNode lit:
                    return lit.Kind == LiteralKind.None ? "None" : lit.Kind == LiteralKind.Ellipsis ? "ellipsis"
                        : lit.Kind == LiteralKind.Bool ? ((bool)lit.Value ? "True" : "False") : "literal";
                case NameNode _: return "name";
                case AttributeNode _: return "attribute";
                case IndexNode _: return "subscript";
                case StarredNode _: return "starred";
                case ListNode _: return "list";
                case TupleNode _: return "tuple";
                case SetNode _: return "set display";
                case DictNode _: return "dict literal";
                case FStringNode _: return "f-string expression";
                case CallNode _: return "function call";
                case BinOpNode _: return "expression";
                case UnaryOpNode _: return "expression";
                case BoolOpNode _: return "expression";
                case CompareChainNode _: return "comparison";
                case IfExpNode _: return "conditional expression";
                case NamedExprNode _: return "named expression";
                case LambdaNode _: return "lambda";
                case ComprehensionNode c:
                    return c.Kind == CompKind.List ? "list comprehension" : c.Kind == CompKind.Set ? "set comprehension"
                        : c.Kind == CompKind.Dict ? "dict comprehension" : "generator expression";
                default: return "expression";
            }
        }
    }
}
