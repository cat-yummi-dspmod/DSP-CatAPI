using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CatAPI
{
    /// <summary>
    /// 方法指纹：把方法的 IL 转成"人类可读的规范化文本"，再对文本做哈希。
    /// 运行时（Harness）和命令行（harness.exe）共用这一份代码，保证两边算出来的结果一致。
    ///
    /// 规范化的目的：游戏更新后 metadata token、IL 偏移几乎都会变，但方法本身没变。
    ///   - 成员引用写成全名（Type::Method），不用 token
    ///   - 分支目标写成"第几条指令"，不用字节偏移
    ///   - 短形式分支（br.s）和长形式（br）视为相同
    /// 只要方法体逻辑、调用的成员、字面量有任何变化，文本就会变。
    ///
    /// Method fingerprint: turns a method's IL into human-readable normalized text, then hashes that text.
    /// The runtime (Harness) and the CLI (harness.exe) share this one file, so both compute identical results.
    ///
    /// Why normalize: after a game update, metadata tokens and IL offsets almost always shift
    /// even though the method itself is unchanged.
    ///   - member references are written as full names (Type::Method), never as tokens
    ///   - branch targets are written as an instruction index, never as a byte offset
    ///   - short-form branches (br.s) and long-form ones (br) are treated as the same
    /// Any change to the method's logic, the members it calls, or its literals changes the text.
    /// </summary>
    public static class Fingerprint
    {
        public static string Describe(MethodDefinition method)
        {
            if (!method.HasBody)
            {
                return method.FullName + "\n<no body>";
            }

            MethodBody body = method.Body;
            var index = new Dictionary<Instruction, int>();
            for (int i = 0; i < body.Instructions.Count; i++)
            {
                index[body.Instructions[i]] = i;
            }

            var sb = new StringBuilder();
            sb.Append(".method ").Append(method.FullName).Append('\n');
            for (int i = 0; i < body.Variables.Count; i++)
            {
                sb.Append(".local V").Append(i).Append(' ').Append(body.Variables[i].VariableType.FullName).Append('\n');
            }
            foreach (ExceptionHandler h in body.ExceptionHandlers)
            {
                sb.Append(".handler ").Append(h.HandlerType)
                  .Append(" try=").Append(Idx(index, h.TryStart)).Append('-').Append(Idx(index, h.TryEnd))
                  .Append(" handler=").Append(Idx(index, h.HandlerStart)).Append('-').Append(Idx(index, h.HandlerEnd));
                if (h.CatchType != null)
                {
                    sb.Append(" catch=").Append(h.CatchType.FullName);
                }
                sb.Append('\n');
            }
            for (int i = 0; i < body.Instructions.Count; i++)
            {
                Instruction ins = body.Instructions[i];
                string op = ins.OpCode.Name;
                if (op.EndsWith(".s", StringComparison.Ordinal))
                {
                    op = op.Substring(0, op.Length - 2);
                }
                sb.Append(i.ToString("D4", CultureInfo.InvariantCulture)).Append("  ").Append(op);
                string operand = Operand(ins.Operand, index);
                if (operand.Length > 0)
                {
                    sb.Append(' ').Append(operand);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string Hash(MethodDefinition method)
        {
            return HashText(Describe(method));
        }

        public static string HashText(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder();
                for (int i = 0; i < 8; i++)
                {
                    sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        private static string Idx(Dictionary<Instruction, int> index, Instruction ins)
        {
            if (ins == null)
            {
                return "end";
            }
            return index.TryGetValue(ins, out int i) ? i.ToString(CultureInfo.InvariantCulture) : "?";
        }

        private static string Operand(object operand, Dictionary<Instruction, int> index)
        {
            switch (operand)
            {
                case null:
                    return "";
                case Instruction target:
                    return "-> " + Idx(index, target);
                case Instruction[] targets:
                    var parts = new string[targets.Length];
                    for (int i = 0; i < targets.Length; i++)
                    {
                        parts[i] = Idx(index, targets[i]);
                    }
                    return "-> " + string.Join(",", parts);
                case string s:
                    return "\"" + s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\"", "\\\"") + "\"";
                case MemberReference member:
                    return member.FullName;
                case VariableDefinition v:
                    return "V" + v.Index.ToString(CultureInfo.InvariantCulture);
                case ParameterDefinition p:
                    return "A" + p.Index.ToString(CultureInfo.InvariantCulture);
                case float f:
                    return f.ToString("R", CultureInfo.InvariantCulture);
                case double d:
                    return d.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable fmt:
                    return fmt.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return operand.ToString();
            }
        }
    }
}
