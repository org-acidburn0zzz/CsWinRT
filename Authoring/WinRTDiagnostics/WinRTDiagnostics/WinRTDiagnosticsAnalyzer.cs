using Microsoft.CodeAnalysis;
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

        /* LogTime 
         *   writes a log file in my root directory  
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

        #region ErrorMessageAndDiagnosticRules

        /* makeLocalizableString - constructor for the objects used in our DiagnosticRule(s)
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

        internal static DiagnosticDescriptor AsyncRule = makeRule(DiagnosticSeverity.Error);

        /* SupportedDiagnostics is used by the analyzer base code I believe -- this array will grow as we add more diagnostics, 
         *   so the getter will need to use Create that takes an array of DiagnosticDescriptor instead of just a single DiagDescr */
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(AsyncRule); } }

        #endregion

        // Contains the fully qualified type name of the async interfaces that Windows Runtime Components should not implement
        private static string[] AsyncInterfaceNames = new string[]{ "Windows.Foundation.IAsyncAction" };

        public void CatchWinRTDiagnostics(CompilationStartAnalysisContext compilationContext)
        {
            AnalyzerConfigOptions configOptions = compilationContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
 
            if (configOptions.TryGetValue("build_property.CsWinRTComponent", out var isCsWinRTComponentStr))
            {
                if (bool.TryParse(isCsWinRTComponentStr, out var isCsWinRTComponent) && !isCsWinRTComponent)
                {
                    /* Don't analyze if not a CsWinRT Component */
                    return;
                }
                
                LogTime("[v4] In CompilationStart, Before SyntaxNode");

                /* Create a diagnostic if any of the Async interfaces are implemented */
                foreach (string asyncInterfaceName in AsyncInterfaceNames)
                { 
                    // todo: read GetTypeByMetadataName ; AsyncActionInterfaceName should vary 
                    INamedTypeSymbol interfaceType = compilationContext.Compilation.GetTypeByMetadataName(asyncInterfaceName); 

                    /* Runtime components should not implement IAsyncAction (and similar) interfaces */
                    compilationContext.RegisterSymbolAction( 
                        symbolContext => { AnalyzeSymbol(symbolContext, interfaceType, asyncInterfaceName); },
                        SymbolKind.NamedType);
                }
            } 
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            /* Following method checks that the project is authoring a CsWinRT component before analyzing */
            context.RegisterCompilationStartAction(CatchWinRTDiagnostics);
        }
 
        // identifies all named types implementing this interface and reports diagnostics for all 
        private static void AnalyzeSymbol(SymbolAnalysisContext context, INamedTypeSymbol interfaceType, string asyncInterfaceName)
        {
            // type cast always succeeds, b/c we call with SymbolKind.NamedType
            INamedTypeSymbol namedType = (INamedTypeSymbol)context.Symbol;
             
            // check if the symbol implements the interface type
            if (namedType.Interfaces.Contains(interfaceType))
            {
                string str = "Found interfaceType on namedType : " + interfaceType.ToString() + " on " + namedType.Name;
                LogTime(str);

                Diagnostic diagnostic = Diagnostic.Create(
                    AsyncRule,
                    namedType.Locations[0],
                    namedType.Name,
                    asyncInterfaceName);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
