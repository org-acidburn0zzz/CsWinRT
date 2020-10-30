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

        /* makeLocalizableString - constructor for the objects used in our DiagnosticRule
         */
        private static LocalizableResourceString makeLocalizableString(string name)
        { 
            return new LocalizableResourceString(name, Resources.ResourceManager, typeof(Resources));
        }

        private static readonly LocalizableString ActionAsyncDiagnosticTitle = makeLocalizableString(nameof(Resources.WME1084AnalyzerTitle));
        private static readonly LocalizableString AsyncDiagnosticMessageFormat = makeLocalizableString(nameof(Resources.WME1084AnalyzerMessageFormat));
        private static readonly LocalizableString AsyncDiagnosticDescription = makeLocalizableString(nameof(Resources.WME1084AnalyzerDescription));
        
        private const string Category = "Usage";

        

        /*  LogTime 
         * * writes a log file in my root gh directory  
        */
        private void LogTime()
        {
            string msg = "[" + DateTime.Now.ToString("HH:mm:ss.ffffzzz" + "]" + " WinRTDiagnosticsAnalyzer Running");
            System.IO.File.WriteAllText(@"C:\gh\analyzerLog.txt", msg);
        }
     
        /* makeRule 
         * * takes either DiagnosticSeverity.Warning or DiagnosticSeverity.Error
         * *  and creates the diagnostic with that severity 
        
         * todo: Figure out the story on the title and format (from the resources file).
         * iirc the results from experiments: Error fails builds, Warning don't get caught by the source generator
        */
        private static DiagnosticDescriptor makeRule(DiagnosticSeverity severity) 
        {
            return new DiagnosticDescriptor(DiagnosticId,
                ActionAsyncDiagnosticTitle,   // <-- either use the existing error message
                AsyncDiagnosticMessageFormat, // <-- or make a customizable one, based on which interface type we were passed 
                Category,
                severity,
                isEnabledByDefault: true, description: AsyncDiagnosticDescription);
        }

        /* SupportedDiagnostics -- used by the package somehow 
         */
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics 
        { 
            get 
            {
                // right now the rules are based 
                DiagnosticDescriptor[] ddArr = { makeRule(DiagnosticSeverity.Warning) };
                var arr = ImmutableArray.Create(ddArr /*AsyncRule*/ ); 
                return arr;
            } 
        }

        /*
         *  Converts a string to a boolean
         */
        private bool componentStrToBool(string isCsWinRTComponentStr)
        {
            if (bool.TryParse(isCsWinRTComponentStr, out var isCsWinRTComponent))
            {
                return isCsWinRTComponent;
            }
            return false;
        }

        private static void AnalyzeSymbol(SymbolAnalysisContext context, INamedTypeSymbol interfaceType)
        {
            // type cast always succeeds, b/c we call with SymbolKind.NamedType
            INamedTypeSymbol namedType = (INamedTypeSymbol)context.Symbol;

            // check if the symbol implements the interface type
            if (namedType.Interfaces.Contains(interfaceType))
            {
                string AsyncInterfaceString = "IAsyncAction";
                Diagnostic diagnostic = Diagnostic.Create(makeRule(DiagnosticSeverity.Error), // was considering having makeRule take a string too, like AsyncInterfaceString
                    namedType.Locations[0],
                    namedType.Name,
                    AsyncInterfaceString);
                context.ReportDiagnostic(diagnostic);
            }

        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            /* Check that the project is authoring a CsWinRT component before analyzing */
            context.RegisterCompilationStartAction(compilationContext => 
            { 
                /* get the project properties */
                AnalyzerConfigOptions configOptions = compilationContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
                
                if (configOptions.TryGetValue("build_property.CsWinRTComponent", out var isCsWinRTComponentStr))
                {
                    /* Don't analyze if not a CsWinRT Component */
                    if (componentStrToBool(isCsWinRTComponentStr))
                    {
                        return;
                    }
                    
                    // For seeing when the analyzer got called
                    LogTime(); 
    
                    // this should vary over a few interface names
                    string asyncInterfaceTypeName = "IAsyncAction"; 
                    
                    // todo: read GetTypeByMetadataName  
                    INamedTypeSymbol interfaceType = compilationContext.Compilation.GetTypeByMetadataName(asyncInterfaceTypeName);
                    
                    /* Runtime components should not implement IAsyncAction (and similar) interfaces */
                    compilationContext.RegisterSymbolAction( 
                        // identifies all named types implementing this interface and reports diagnostics for all 
                        symbolContext => { AnalyzeSymbol(symbolContext, interfaceType); },
                        SymbolKind.NamedType); 
                }
            });
        }
    }
}
