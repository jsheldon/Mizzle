; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MIZ001 | Mizzle | Error | Column dialect does not match table
MIZ002 | Mizzle | Error | Query terminator is not interceptable in Strict mode
MIZ003 | Mizzle | Error | Selected column has no matching member on the projection target
MIZ004 | Mizzle | Error | Required member of the projection target has no matching column
MIZ005 | Mizzle | Error | Nullable column mapped to a non-nullable member
MIZ006 | Mizzle | Error | Ambiguous projection member match
MIZ007 | Mizzle | Error | Cannot generate projection type: query shape not statically visible
MIZ008 | Mizzle | Error | Column converter must be a static method reference
MIZ009 | Mizzle | Error | Column converter result type is nullable
MIZ010 | Mizzle | Error | Selected column type does not match the projection member type
MIZ011 | Mizzle | Error | Two tables in one query share an alias
MIZ012 | Mizzle | Error | WithAlias requires a parameterless constructor
