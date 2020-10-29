using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace WinRTDiagnostics
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class WinRTDiagnosticsAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "WinRTDiagnostics";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString AsyncDiagnosticTitle = new LocalizableResourceString(nameof(Resources.WME1084AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString AsyncDiagnosticMessageFormat = new LocalizableResourceString(nameof(Resources.WME1084AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString AsyncDiagnosticDescription = new LocalizableResourceString(nameof(Resources.WME1084AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor AsyncRule = new DiagnosticDescriptor(DiagnosticId, 
            AsyncDiagnosticTitle, AsyncDiagnosticMessageFormat, Category, 
            DiagnosticSeverity.Error, isEnabledByDefault: true, description: AsyncDiagnosticDescription);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(AsyncRule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.None);

            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information

            /* Check that the project is authoring a CsWinRT component before analyzing */
            context.RegisterCompilationStartAction(compilationContext =>
            {
                AnalyzerConfigOptions configOptions = compilationContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
                if (configOptions.TryGetValue("build_property.CsWinRTComponent", out var isCsWinRTComponentStr))
                {
                    var success = bool.TryParse(isCsWinRTComponentStr, out var isCsWinRTComponent) && isCsWinRTComponent;
                    if (!success)
                    {
                        /* Don't analyze if not a CsWinRT Component */
                        return;
                    }
                    /* Runtime components should not implement IAsyncAction (and similar) interfaces */
                    context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.SimpleBaseType);
                }
            });
        }

        /* consider the following as an alternative method:
           https://www.meziantou.net/working-with-types-in-a-roslyn-analyzer.htm */
        private void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            var baseType = (SimpleBaseTypeSyntax)context.Node;
            foreach (SyntaxNode node in baseType.ChildNodes())
            {
                if (node.IsKind(SyntaxKind.IdentifierName))
                {
                    var identifierToken_Ithink = node.GetFirstToken().ToString();
                    if (identifierToken_Ithink == "IAsyncAction")
                    {
                        context.ReportDiagnostic(Diagnostic.Create(AsyncRule, context.Node.GetLocation()));
                    }
                }
                else if (node.IsKind(SyntaxKind.GenericName))
                {
                    var genericIdentifierToken_Ithink = node.GetFirstToken().ToString(); // maybe use .Text instead of ToString() ??
                    if (genericIdentifierToken_Ithink == "IAsyncActionWithProgress")
                    {
                        context.ReportDiagnostic(Diagnostic.Create(AsyncRule, context.Node.GetLocation()));
                    }
                }
            }
        }
    }
}
