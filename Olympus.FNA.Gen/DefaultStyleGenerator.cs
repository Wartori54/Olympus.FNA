using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace Olympus.Gen {
    [Generator]
    public class DefaultStyleGenerator : IIncrementalGenerator {
        
        public void Initialize(IncrementalGeneratorInitializationContext context) {
#if DEBUG && false
            if (!Debugger.IsAttached)
                Debugger.Launch();
#endif

            IncrementalValuesProvider<ClassDeclarationSyntax?> decls =
                context.SyntaxProvider.CreateSyntaxProvider(
                    static (node, ct) => IsSyntaxTargetForGeneration(node, ct),
                    static (ctx, ct) => GetSemanticTargetForGeneration(ctx, ct)
                ).Where(static m => m is not null);

            IncrementalValueProvider<(Compilation Left, ImmutableArray<ClassDeclarationSyntax?> Right)> src =
                context.CompilationProvider.Combine(decls.Collect());

            context.RegisterSourceOutput(src, static (spc, src) => Execute(src.Left, src.Right, spc));
        }

        static bool IsSyntaxTargetForGeneration(SyntaxNode node, CancellationToken ct)
            => node is FieldDeclarationSyntax field &&
                field.Declaration.Type is QualifiedNameSyntax typeFull && (typeFull.Left as SimpleNameSyntax)?.Identifier.Text == "Style" && typeFull.Right.Identifier.Text == "Entry" &&
                field.Declaration.Variables.FirstOrDefault() is VariableDeclaratorSyntax varNode && varNode.Identifier.Text.StartsWith("Style");

        static ClassDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext ctx, CancellationToken ct) {
            FieldDeclarationSyntax field = (FieldDeclarationSyntax) ctx.Node;

            bool isFieldProtected = false;
            foreach (SyntaxToken mod in field.Modifiers) {
                if (mod.Text == "protected") {
                    isFieldProtected = true;
                    break;
                }
            }

            if (!isFieldProtected)
                return null;

            if (field.Parent is not ClassDeclarationSyntax type)
                return null;

            // only in partial classes (and partial subclasses of partial classes)
            for (ClassDeclarationSyntax? parent = type; parent != null; parent = parent.Parent as ClassDeclarationSyntax) {
                bool isTypePartial = false;
                foreach (SyntaxToken mod in parent.Modifiers) {
                    if (mod.Text == "partial") {
                        isTypePartial = true;
                        break;
                    }
                }

                if (!isTypePartial)
                    return null;
            }

            return type;
        }

        private static void Execute(Compilation comp, ImmutableArray<ClassDeclarationSyntax?> types, SourceProductionContext ctx) {
            if (types.IsDefaultOrEmpty)
                return;

            StringBuilder builder = new();
            List<(string Visibility, string Name)> nests = new();
            List<(string Name, string Value)> entries = new();

            foreach (ClassDeclarationSyntax? elType in types.Distinct()) {
                if (elType is null)
                    continue;

                builder.Clear();
                nests.Clear();
                entries.Clear();

                string baseType = "Element";
                if (elType.BaseList?.ChildNodes().FirstOrDefault() is SimpleBaseTypeSyntax baseTypeSyntax &&
                    baseTypeSyntax.Type is SimpleNameSyntax baseTypeIdentifier)
                    baseType = baseTypeIdentifier.Identifier.Text;

                bool containsExplicitDefaultStyle = false;
                foreach (MemberDeclarationSyntax member in elType.Members) {
                    if (member is not FieldDeclarationSyntax field)
                        continue;

                    if (field.Declaration.Variables.FirstOrDefault() is VariableDeclaratorSyntax varname && varname.Identifier.Text == "DefaultStyle") {
                        containsExplicitDefaultStyle = true;
                        break;
                    }
                }

                foreach (MemberDeclarationSyntax member in elType.Members) {
                    if (member is not FieldDeclarationSyntax field)
                        continue;

                    if (field.Declaration.Variables.FirstOrDefault() is not VariableDeclaratorSyntax varNode ||
                        varNode.Identifier.Text is not string name || !name.StartsWith("Style") ||
                        varNode.Initializer is not EqualsValueClauseSyntax equalsValue)
                        continue;

                    if (equalsValue.Value is not ImplicitObjectCreationExpressionSyntax inst ||
                        inst.ArgumentList.Arguments.Count != 1)
                        continue;

                    entries.Add((name.Substring(5), inst.ArgumentList.Arguments[0].ToString()));
                }

                if (entries.Count == 0)
                    continue;

                if (elType.SyntaxTree is not SyntaxTree tree)
                    continue;
                SyntaxNode root = tree.GetRoot(ctx.CancellationToken);

                string hint;
                string? ns = null;
                {
                    BaseNamespaceDeclarationSyntax? nsNode = null;

                    for (ClassDeclarationSyntax? type = elType; type is not null; type = type.Parent as ClassDeclarationSyntax) {
                        string name = type.Identifier.Text;
                        nests.Add((type.Modifiers[0].Text, name));
                        builder.Insert(0, name);
                        if ((nsNode = type.Parent as BaseNamespaceDeclarationSyntax) is null)
                            builder.Insert(0, "+");
                    }

                    if (nsNode != null && nsNode.Name is SimpleNameSyntax nsName) {
                        ns = nsName.Identifier.Text;
                        builder.Insert(0, ".");
                        builder.Insert(0, ns);
                    }

                    builder.Append(".DefaultStyle.g.cs");
                    hint = builder.ToString();
                    builder.Clear();
                }

                {
                    int indentLevel = 0;
                    bool indented = false;
                    void AppendIndent() {
                        if (indented)
                            return;
                        for (int i = indentLevel; i > 0; --i) {
                            builder.Append("    ");
                        }
                        indented = true;
                    }

                    void AppendLine(string? txt = null) {
                        if (!string.IsNullOrWhiteSpace(txt)) {
                            AppendIndent();
                            builder.AppendLine(txt);
                        } else {
                            builder.AppendLine();
                        }
                        indented = false;
                    }

#pragma warning disable CS8321
                    void Append(string txt) {
                        AppendIndent();
                        builder.Append(txt);
                    }
#pragma warning restore CS8321

                    AppendLine("// Auto-generated by Olympus.FNA.Gen");

                    bool hasUsings = false;
                    foreach (SyntaxNode node in root.ChildNodes()) {
                        if (node is not UsingDirectiveSyntax usage)
                            continue;
                        AppendLine(usage.ToString());
                        hasUsings = true;
                    }

                    if (hasUsings) {
                        AppendLine();
                    }

                    if (!string.IsNullOrEmpty(ns)) {
                        AppendLine($"namespace {ns} {{");
                        indentLevel++;
                    }
                    for (int i = nests.Count - 1; i >= 0; --i) {
                        (string visibility, string name) = nests[i];
                        AppendLine($"{visibility} partial class {name} {{");
                        indentLevel++;
                    }

                    if (!containsExplicitDefaultStyle) {
                        AppendLine();
                        AppendLine("public static readonly new Style DefaultStyle = new() {");
                        indentLevel++;
                        foreach ((string name, string value) in entries) {
                            AppendLine($"{{ StyleKeys.{name}, {value} }},");
                        }
                        indentLevel--;
                        AppendLine("};");
                        AppendLine();

                    } else {
                        AppendLine();
                        AppendLine($"static {nests[0].Name}() {{");
                        indentLevel++;
                        foreach ((string name, string value) in entries) {
                            AppendLine($"DefaultStyle.Add(StyleKeys.{name}, {value});");
                        }
                        indentLevel--;
                        AppendLine("}");
                        AppendLine();
                    }

                    AppendLine();
                    AppendLine("protected override void SetupStyleEntries() {");
                    indentLevel++;
                    AppendLine("base.SetupStyleEntries();");
                    foreach ((string name, _) in entries) {
                        AppendLine($"Style{name} = Style.GetEntry(StyleKeys.{name});");
                    }
                    indentLevel--;
                    AppendLine("}");
                    AppendLine();

                    AppendLine();
                    AppendLine($"public new abstract partial class StyleKeys : {baseType}.StyleKeys {{");
                    indentLevel++;

                    AppendLine();
                    AppendLine("protected StyleKeys(Secret secret) : base(secret) { }");
                    AppendLine();

                    AppendLine();
                    foreach ((string name, _) in entries) {
                        // FIXME: Avoid generating style keys for entries which also exist in parents.
                        AppendLine($"public static readonly Style.Key {name} = new(\"{name}\");");
                    }
                    AppendLine();

                    indentLevel--;
                    AppendLine("}");
                    AppendLine();

                    foreach ((string Visibility, string Name) nest in nests) {
                        indentLevel--;
                        AppendLine("}");
                    }
                    if (!string.IsNullOrEmpty(ns)) {
                        indentLevel--;
                        AppendLine("}");
                    }

                    builder.Replace("\r\n", "\n");
                }

                ctx.AddSource(hint, builder.ToString());
            }
        }

    }
}
