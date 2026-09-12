using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // Iterative validation of an assignment target (no C# recursion over user data, R-PAR-07).
        private void ValidateAssignTarget(ExprNode root)
        {
            var stack = new Stack<ExprNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ExprNode t = stack.Pop();
                if (t is NameNode || t is AttributeNode || t is IndexNode)
                    continue;
                TupleNode tn = t as TupleNode;
                ListNode ln = t as ListNode;
                if (tn != null)
                {
                    PushTargetSeq(stack, tn.Elts, t);
                    continue;
                }

                if (ln != null)
                {
                    PushTargetSeq(stack, ln.Elts, t);
                    continue;
                }

                if (t is StarredNode)
                    throw ErrorAtSrc(t.Src, "starred assignment target must be in a list or tuple");
                throw ErrorAtSrc(t.Src, "can't assign to " + Describe(t));
            }
        }

        private void PushTargetSeq(Stack<ExprNode> stack, IReadOnlyList<ExprNode> elts, ExprNode container)
        {
            int starred = 0;
            foreach (ExprNode elt in elts)
            {
                StarredNode s = elt as StarredNode;
                if (s != null)
                {
                    starred++;
                    stack.Push(s.Value);
                }
                else
                    stack.Push(elt);
            }
            if (starred > 1)
                throw ErrorAtSrc(container.Src, "two starred expressions in assignment");
        }

        private void ValidateAugTarget(ExprNode lhs)
        {
            if (lhs is NameNode || lhs is AttributeNode || lhs is IndexNode)
                return;
            throw ErrorAtSrc(lhs.Src, "illegal expression for augmented assignment");
        }

        private void ValidateDelTarget(ExprNode root)
        {
            var stack = new Stack<ExprNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ExprNode t = stack.Pop();
                if (t is NameNode || t is AttributeNode || t is IndexNode)
                    continue;
                TupleNode tn = t as TupleNode;
                ListNode ln = t as ListNode;
                if (tn != null)
                {
                    foreach (var e in tn.Elts)
                        stack.Push(e);
                    continue;
                }

                if (ln != null)
                {
                    foreach (var e in ln.Elts)
                        stack.Push(e);
                    continue;
                }

                if (t is StarredNode)
                    throw ErrorAtSrc(t.Src, "can't use starred expression in del");
                throw ErrorAtSrc(t.Src, "can't delete " + Describe(t));
            }
        }

        private static string Describe(ExprNode t)
        {
            switch (t)
            {
                case LiteralNode _: return "literal";
                case FStringNode _: return "literal";
                case CallNode _: return "function call";
                case BinOpNode _: return "operator";
                case UnaryOpNode _: return "operator";
                case BoolOpNode _: return "operator";
                case CompareChainNode _: return "operator";
                case ComprehensionNode _: return "comprehension";
                case IfExpNode _: return "conditional expression";
                case LambdaNode _: return "lambda";
                default: return "expression";
            }
        }
    }
}
