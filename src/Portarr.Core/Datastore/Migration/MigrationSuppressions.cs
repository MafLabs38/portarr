using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Minor Code Smell",
    "S1192:String literals should not be duplicated",
    Scope = "namespaceanddescendants",
    Target = "~N:Portarr.Core.Datastore.Migration",
    Justification = "A migration is a frozen description of the schema: table and column names stay literals, in the DSL's reading order, so that it never follows a later change of the code.")]
