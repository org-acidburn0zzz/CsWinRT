﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace WinRTDiagnostics
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class WinRTDiagnosticsAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "WinRTDiagnostics";

        /*  LogTime 
         *  * * writes a log file in my root gh directory  
         */
        private static void LogTime(string str)
        {
            string path = @"C:\gh\analyzerLog.txt";
            string msg = "[" + DateTime.Now.ToString("HH:mm:ss.ffffzzz" + "]" + " WinRTDiagnosticsAnalyzer Running. " + str);

            using (StreamWriter sw = System.IO.File.AppendText(path))
            {
                sw.WriteLine(msg);
            }
        }

        /* makeLocalizableString - constructor for the objects used in our DiagnosticRule
        */
        private static LocalizableResourceString makeLocalizableString(string name)
        {
            return new LocalizableResourceString(name, Resources.ResourceManager, typeof(Resources));
        }

        private static readonly LocalizableString AsyncDiagnosticTitle = makeLocalizableString(nameof(Resources.WME1084AnalyzerTitle));
        private static readonly LocalizableString AsyncDiagnosticMessageFormat = makeLocalizableString(nameof(Resources.WME1084AnalyzerMessageFormat));
        private static readonly LocalizableString AsyncDiagnosticDescription = makeLocalizableString(nameof(Resources.WME1084AnalyzerDescription));
        private const string Category = "Usage";

        
        /* makeRule 
        * * takes either DiagnosticSeverity.Warning or DiagnosticSeverity.Error
        * *  and creates the diagnostic with that severity 
        * todo: Figure out the story on the title and format (from the resources file).
        *   either use the existing error message (from docs on diagnostics) or make a customizable one that takes the interface type as parameter
        *   
        * iirc the results from experiments: Error fails builds, Warning don't get caught by the source generator */
        private static DiagnosticDescriptor makeRule(DiagnosticSeverity severity)
        {
            return new DiagnosticDescriptor(DiagnosticId,
                AsyncDiagnosticTitle,          // see todo
                AsyncDiagnosticMessageFormat,  // "   "
                Category,
                severity,
                isEnabledByDefault: true, description: AsyncDiagnosticDescription);
        }

        /* SupportedDiagnostics is used by the analyzer base code I believe -- this array will grow as we add more diagnostics, 
         *   so the getter will need to use Create that takes an array of DiagnosticDescriptor instead of just a single DiagDescr */
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(makeRule(DiagnosticSeverity.Error)); } }


        private static string AsyncActionInterfaceName = "Windows.Foundation.IAsyncAction";
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information

            /* Check that the project is authoring a CsWinRT component before analyzing */
            context.RegisterCompilationStartAction(compilationContext =>
            {
                AnalyzerConfigOptions configOptions = compilationContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions;

                if (configOptions.TryGetValue("build_property.CsWinRTComponent", out var isCsWinRTComponentStr))
                {
                    if (bool.TryParse(isCsWinRTComponentStr, out var isCsWinRTComponent) && !isCsWinRTComponent)
                    {
                        /* Don't analyze if not a CsWinRT Component */
                        return;
                    }

                    LogTime("In CompilationStart, Before SyntaxNode");

                    // todo: read GetTypeByMetadataName ; AsyncActionInterfaceName should vary 
                    INamedTypeSymbol interfaceType = compilationContext.Compilation.GetTypeByMetadataName(AsyncActionInterfaceName);

                    /* Runtime components should not implement IAsyncAction (and similar) interfaces */
                    compilationContext.RegisterSymbolAction(
                        // identifies all named types implementing this interface and reports diagnostics for all 
                        symbolContext => { AnalyzeSymbol(symbolContext, interfaceType); },
                        SymbolKind.NamedType);
                }
            });
        }

        private static void AnalyzeSymbol(SymbolAnalysisContext context, INamedTypeSymbol interfaceType)
        {
            // type cast always succeeds, b/c we call with SymbolKind.NamedType
            INamedTypeSymbol namedType = (INamedTypeSymbol)context.Symbol;
 
            // check if the symbol implements the interface type
            if (namedType.Interfaces.Contains(interfaceType))
            {
                string str = "Found interfaceType on namedType : " + interfaceType.ToString() + " on " + namedType.Name;
                LogTime(str);

                Diagnostic diagnostic = Diagnostic.Create(
                    makeRule(DiagnosticSeverity.Error), 
                    namedType.Locations[0],
                    namedType.Name,
                    AsyncActionInterfaceName);

                context.ReportDiagnostic(diagnostic);
            }
        }
       
        /*
        private void ReportIfInterface(string asyncInterface, SyntaxNode node, SyntaxNodeAnalysisContext context)

        {
            string interfaceName = node.GetFirstToken().ToString();
            if (interfaceName == asyncInterface)
            {
                context.ReportDiagnostic(Diagnostic.Create(makeRule(DiagnosticSeverity.Error), context.Node.GetLocation()));
            }
        }

        private void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            // Logging temporarily 
            // LogTime("Starting AnalyzeNode");
            var baseType = (SimpleBaseTypeSyntax)context.Node;
            foreach (SyntaxNode node in baseType.ChildNodes())
            {
                if (node.IsKind(SyntaxKind.IdentifierName))
                {
                    ReportIfInterface("IAsyncAction", node, context);
                }
                else if (node.IsKind(SyntaxKind.GenericName))
                {
                    ReportIfInterface("IAsyncActionWithProgress", node, context);
                }
            }
        }
        */
    }
}
