using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Mizzle.Generators;
using Mizzle.Postgres;
using Mizzle.Schema;
using Mizzle.SqlServer;

namespace Mizzle.Generators.Tests;

internal static class GeneratorTestHost
{
    public static GeneratorDriverRunResult Run(params string[] sources)
    {
        var parseOptions = ParseOptions();
        var compilation = CreateCompilation(sources, parseOptions);
        var driver = CSharpGeneratorDriver.Create(
            [
                new SchemaGenerator().AsSourceGenerator(),
                new QueryInterceptorGenerator().AsSourceGenerator(),
                new ProjectionGenerator().AsSourceGenerator()
            ],
            parseOptions: parseOptions);
        return driver.RunGenerators(compilation).GetRunResult();
    }

    public static (GeneratorDriverRunResult Result, ImmutableArray<Diagnostic> CompileDiagnostics) RunAndCompile(params string[] sources)
    {
        var parseOptions = ParseOptions();
        var compilation = CreateCompilation(sources, parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [
                new SchemaGenerator().AsSourceGenerator(),
                new QueryInterceptorGenerator().AsSourceGenerator(),
                new ProjectionGenerator().AsSourceGenerator()
            ],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output.GetDiagnostics());
    }

    public static ImmutableArray<Diagnostic> Analyze(string source, string? queryMode)
    {
        var parseOptions = ParseOptions();
        var compilation = CreateCompilation([source], parseOptions);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new StrictAnalyzer());
        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new TestAnalyzerConfigOptionsProvider(queryMode));
        return compilation.WithAnalyzers(analyzers, options).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    public static string Generated(GeneratorDriverRunResult result)
        => string.Join("\n", result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

    public static CSharpCompilation Compile(string source)
        => CreateCompilation([source], ParseOptions());

    private static CSharpParseOptions ParseOptions()
        => new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Mizzle.Generated.Interceptors")]);

    private static CSharpCompilation CreateCompilation(string[] sources, CSharpParseOptions parseOptions)
        => CSharpCompilation.Create(
            "GeneratorTests",
            [..sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions))],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> References()
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));

        return
        [
            ..platform,
            MetadataReference.CreateFromFile(typeof(PgTable<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SqlTable<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ITable).Assembly.Location)
        ];
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options;

        public TestAnalyzerConfigOptionsProvider(string? queryMode)
            => _options = new TestAnalyzerConfigOptions(queryMode);

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly string? _queryMode;

        public TestAnalyzerConfigOptions(string? queryMode) => _queryMode = queryMode;

        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_property.MizzleQueryMode" && _queryMode is not null)
            {
                value = _queryMode;
                return true;
            }

            value = "";
            return false;
        }
    }
}
